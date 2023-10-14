namespace Runner.Server.Azure.Devops
{
    // TODO: Move to Sdk.AzurePipelines folder
    public static class PipelineExtensions
    {
        public static string ToYaml(this Pipeline pipeline)
        {
            try
            {
                // convert back to JToken
                var newcontent = pipeline.ToContextData().ToJToken().ToString();

                // serialize back to YAML
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
                var serializer = new YamlDotNet.Serialization.SerializerBuilder().WithEventEmitter(emitter =>
                {
                    return new MyEventEmitter(emitter);
                }).Build();
                return serializer.Serialize(deserializer.Deserialize<Object>(newcontent));
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private class MyEventEmitter : YamlDotNet.Serialization.EventEmitters.ChainedEventEmitter
        {
            public MyEventEmitter(YamlDotNet.Serialization.IEventEmitter emitter) : base(emitter)
            {
            }

            private class ReplaceDescriptor : YamlDotNet.Serialization.IObjectDescriptor
            {
                public object? Value { get; set; }
                public Type Type { get; set; }
                public Type StaticType { get; set; }
                public YamlDotNet.Core.ScalarStyle ScalarStyle { get; set; }
            }

            public override void Emit(YamlDotNet.Serialization.ScalarEventInfo eventInfo, YamlDotNet.Core.IEmitter emitter)
            {
                if (eventInfo.Source.Value is string svalue)
                {
                    // Apply expression escaping to allow parsing the result without errors
                    if (svalue.Contains("${{"))
                    {
                        eventInfo = new YamlDotNet.Serialization.ScalarEventInfo(new ReplaceDescriptor { Value = svalue.Replace("${{", "${{ '${{' }}"), Type = eventInfo.Source.Type, StaticType = eventInfo.Source.StaticType, ScalarStyle = eventInfo.Source.ScalarStyle });
                    }
                    if (svalue.Contains('\n'))
                    {
                        eventInfo.Style = YamlDotNet.Core.ScalarStyle.Literal;
                        eventInfo.IsPlainImplicit = false;
                        eventInfo.IsQuotedImplicit = false;
                    }
                }
                base.Emit(eventInfo, emitter);
            }
        }
    }
}
