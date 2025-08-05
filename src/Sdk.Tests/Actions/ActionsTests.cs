using GitHub.DistributedTask.Expressions2;
using GitHub.DistributedTask.ObjectTemplating;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.Pipelines.ObjectTemplating;

namespace Runner.Server
{
    public class ActionsTests
    {
        private static TemplateContext CreateTemplateContext(GitHub.DistributedTask.ObjectTemplating.ITraceWriter traceWriter) {
            ExpressionFlags flags = ExpressionFlags.None;

            var templateContext = new TemplateContext() {
                Flags = flags,
                CancellationToken = CancellationToken.None,
                Errors = new TemplateValidationErrors(10, 500),
                Memory = new TemplateMemory(
                    maxDepth: 100,
                    maxEvents: 1000000,
                    maxBytes: 10 * 1024 * 1024),
                TraceWriter = traceWriter,
                Schema = PipelineTemplateSchemaFactory.GetSchema()
            };
            return templateContext;
        }

        [Fact]
        public void TestParseYamlAnchorsComplex()
        {
      using var content = new StringReader(@"
name: 'Test Workflow'
on: [push, pull_request]
jobs:
  build:
    runs-on: &name ubuntu-latest
    steps:
      - &anchor
        name: Checkout
        uses: actions/checkout@v4
      - name: Run Tests
        run: echo 'Running tests...'
  build2:
    runs-on: ubuntu-latest
    steps:
      - *anchor
      - *anchor
      - *anchor
      - name: Run Tests
        run: echo 'Running tests...'
  *name:
    runs-on: ubuntu-latest
    steps:
      - *anchor
      - *anchor
      - *anchor
");

            YamlObjectReader reader = new YamlObjectReader(null, content, true);
            var ctx = CreateTemplateContext(new EmptyTraceWriter());
            var result = TemplateReader.Read(ctx, "workflow-root", reader, null, out _);
            Assert.Equal(0, ctx.Errors.Count);
            Assert.NotNull(result);
            string f = result.ToContextData().ToJToken().ToString();
            Console.WriteLine(f);
        }
        
        [Fact]
        public void TestParseYamlAnchorsSimple()
        {
      using var content = new StringReader(@"
- &anchor
  key: value
- *anchor
");

            YamlObjectReader reader = new YamlObjectReader(null, content, true);
            var ctx = CreateTemplateContext(new EmptyTraceWriter());
            var result = TemplateReader.Read(ctx, "any", reader, null, out _);
            Assert.Equal(0, ctx.Errors.Count);
            Assert.NotNull(result);
            string f = result.ToContextData().ToJToken().ToString();
            Console.WriteLine(f);
        }
        
        [Fact]
        public void TestParseYamlAnchorsSimpleSplitByMapping()
        {
      using var content = new StringReader(@"
a:
- &anchor
  key: value
b:
- *anchor
");

            YamlObjectReader reader = new YamlObjectReader(null, content, true);
            var ctx = CreateTemplateContext(new EmptyTraceWriter());
            var result = TemplateReader.Read(ctx, "any", reader, null, out _);
            Assert.Equal(0, ctx.Errors.Count);
            Assert.NotNull(result);
            string f = result.ToContextData().ToJToken().ToString();
            Console.WriteLine(f);
        }
    }
}