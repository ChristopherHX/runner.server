using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Microsoft.Extensions.FileProviders;
using Runner.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OpenIdConnect.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Server
{
    public class Startup
    {
        public static RSAParameters AccessTokenParameter;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers(options => {
                // options.InputFormatters.Add(new LogFormatter());
            }).AddNewtonsoftJson();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Runner.Server", Version = "v1" });
                // add JWT Authentication
                var securityScheme = new OpenApiSecurityScheme
                {
                    Name = "JWT Authentication",
                    Description = "Enter JWT Bearer token **_only_**",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer", // must be lower case
                    BearerFormat = "JWT",
                    Reference = new OpenApiReference
                    {
                        Id = JwtBearerDefaults.AuthenticationScheme,
                        Type = ReferenceType.SecurityScheme
                    }
                };
                c.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {securityScheme, new string[] { }}
                });

            });
            var sqlitecon = Configuration.GetConnectionString("sqlite");
            Action<DbContextOptionsBuilder> optionsAction = conf => {
                if(sqlitecon?.Length > 0) {
                    conf.UseSqlite(sqlitecon);
                }
                else {
                    conf.UseInMemoryDatabase("Agents");
                }
            };
            services.AddDbContext<SqLiteDb>(optionsAction);
            
            try {
                var b = new DbContextOptionsBuilder<SqLiteDb>();
                optionsAction(b);
                new SqLiteDb(b.Options).Database.Migrate();
            } catch (Exception ex) {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                // This always throws, if using the InMemory Database
            }
            var sessionCookieLifetime = Configuration.GetValue("SessionCookieLifetimeMinutes", 60);
            services.AddSingleton<IAuthorizationHandler, DevModeOrAuthenticatedUser>();
            services.AddAuthorization(options => {

                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new DevModeOrAuthenticatedUserRequirement()).Build();
                // options.AddPolicy("DevModeOrAuthenticatedUser", builder => builder
                //     .AddRequirements(new DevModeOrAuthenticatedUserRequirement()));
                options.AddPolicy("AgentManagement", policy => policy.RequireClaim("Agent", "management"));
                options.AddPolicy("Agent", policy => policy.RequireClaim("Agent", "oauth"));
                options.AddPolicy("AgentJob", policy => policy.RequireClaim("Agent", "oauth", "job"));
            });
            var rsa = RSA.Create();
            AccessTokenParameter = rsa.ExportParameters(true);
            var auth = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(setup => {
                setup.ExpireTimeSpan = TimeSpan.FromMinutes(sessionCookieLifetime);
                setup.Events.OnValidatePrincipal = context => {
                    var httpContext = context.HttpContext;
                    httpContext.Items["Properties"] = context.Properties;
                    httpContext.Features.Set(context.Properties);
                    return Task.CompletedTask;
                };
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters() {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(rsa),
                    ValidIssuer = "http://githubactionsserver",
                    ValidAudience = "http://githubactionsserver",
                };
            });

            if(Configuration["Authority"] != null && Configuration["ClientId"] != null  && Configuration["ClientSecret"] != null) {
                auth.AddOpenIdConnect(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters() {
                        ValidateActor = false,
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateIssuerSigningKey = false,
                        ValidateLifetime = false,
                        ValidateTokenReplay = false,
                        SignatureValidator = delegate (string token, TokenValidationParameters parameters)
                        {
                            var jwt = new JwtSecurityToken(token);
                            return jwt;
                        }
                    };
                    // options.Events.OnTokenValidated = async ctx => {
                    //     ctx.Principal
                    // };
                    // options.SecurityTokenValidator = new Validator();
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.Authority = Configuration["Authority"];
                    options.SignedOutRedirectUri = "https://localhost";
                    options.ClientId = Configuration["ClientId"];
                    options.ClientSecret = Configuration["ClientSecret"];
                    options.ResponseType = "code";
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.RequireHttpsMetadata = false;
                    options.Scope.Add("openid");
                });
            }

            services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                                builder =>
                                {
                                    builder.AllowAnyOrigin();
                                    builder.AllowAnyHeader();
                                    builder.AllowAnyMethod();
                                });
            });
            services.AddMemoryCache();
            services.Configure<KestrelServerOptions>(options => {
                options.Limits.MaxRequestBodySize = int.MaxValue;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            var pipe = Environment.GetEnvironmentVariable("RUNNER_CLIENT_PIPE");
            if(pipe != null) {
                lifetime.ApplicationStarted.Register(() => {
                    var addr = app.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First();
                    using (PipeStream pipeClient = new AnonymousPipeClientStream(PipeDirection.Out, pipe))
                    {
                        Console.WriteLine("[CLIENT] Current TransmissionMode: {0}.", pipeClient.TransmissionMode);

                        using (StreamWriter sr = new StreamWriter(pipeClient))
                        {
                            sr.WriteLine(addr);
                            // pipeClient.WaitForPipeDrain();
                        }
                    }
                });
            }
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Runner.Server v1"));
            }

            // app.UseHttpsRedirection();

            app.UseRouting();

            app.UseCors(MyAllowSpecificOrigins);

            app.UseAuthentication();
            app.UseAuthorization();
    
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            DefaultFilesOptions options = new DefaultFilesOptions();
            options.DefaultFileNames.Clear();
            options.DefaultFileNames.Add("index.html");
            app.UseDefaultFiles(options);
            app.UseStaticFiles();
        }
    }
}
