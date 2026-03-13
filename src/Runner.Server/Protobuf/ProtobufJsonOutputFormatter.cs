using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Runner.Server {

    public class ProtobufOutputFormatter : OutputFormatter
    {
        private readonly JsonFormatter _formatter;

        public ProtobufOutputFormatter()
        {
            SupportedMediaTypes.Add("application/json");
            _formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation().WithPreserveProtoFieldNames(true).WithFormatDefaultValues(false));
        }

        protected override bool CanWriteType(Type type)
        {
            return typeof(IMessage).IsAssignableFrom(type);
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
        {
            var message = (IMessage)context.Object;
            context.HttpContext.Response.ContentType = new ContentType("application/json") { CharSet = "utf-8" }.ToString();
            await context.HttpContext.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(_formatter.Format(message)), context.HttpContext.RequestAborted);
        }
    }
}