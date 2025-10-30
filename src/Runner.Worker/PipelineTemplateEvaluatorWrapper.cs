using System;
using System.Collections.Generic;
using GitHub.Actions.WorkflowParser;
using GitHub.DistributedTask.Expressions2;
using GitHub.DistributedTask.ObjectTemplating.Tokens;
using GitHub.DistributedTask.Pipelines;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.Pipelines.ObjectTemplating;
using GitHub.DistributedTask.WebApi;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;
using ObjectTemplating = GitHub.DistributedTask.ObjectTemplating;

namespace GitHub.Runner.Worker
{
    internal sealed class PipelineTemplateEvaluatorWrapper : IPipelineTemplateEvaluator
    {
        private readonly PipelineTemplateEvaluator _legacyEvaluator;
        private readonly WorkflowTemplateEvaluator _newEvaluator;
        private readonly bool _compare;
        private readonly IExecutionContext _context;

        public PipelineTemplateEvaluatorWrapper(
            IExecutionContext context,
            ObjectTemplating.ITraceWriter traceWriter = null)
        {
            ArgUtil.NotNull(context, nameof(context));
            _context = context;

            if (traceWriter == null)
            {
                traceWriter = context.ToTemplateTraceWriter();
            }

            // Compare?
            _compare = context.Global.Variables.GetBoolean(Constants.Runner.Features.CompareTemplateEvaluator) ?? false;

            // Legacy evaluator
            var schema = PipelineTemplateSchemaFactory.GetSchema();
            _legacyEvaluator = new PipelineTemplateEvaluator(traceWriter, schema, context.Global.FileTable)
            {
                MaxErrorMessageLength = int.MaxValue, // Don't truncate error messages otherwise we might not scrub secrets correctly
            };

            // New evaluator
            var newTraceWriter = new GitHub.Actions.WorkflowParser.ObjectTemplating.EmptyTraceWriter();
            _newEvaluator = new WorkflowTemplateEvaluator(newTraceWriter, context.Global.FileTable, features: null)
            {
                MaxErrorMessageLength = int.MaxValue, // Don't truncate error messages otherwise we might not scrub secrets correctly
            };
        }

        public Boolean EvaluateStepContinueOnError(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            var legacyResult = _legacyEvaluator.EvaluateStepContinueOnError(token, contextData, expressionFunctions);

            if (_compare)
            {
                try
                {
                    // Note: The new evaluator doesn't have an exact equivalent method yet
                    // When it does, compare results here and add telemetry if different:
                    // if (legacyResult != newResult)
                    // {
                    //     var telemetry = new JobTelemetry { Type = "TemplateEvaluatorMismatch", Message = "EvaluateStepContinueOnError" };
                    //     _context.Global.JobTelemetry.Add(telemetry);
                    // }
                }
                catch (Exception)
                {
                    // Silently ignore comparison errors
                }
            }

            return legacyResult;
        }

        public String EvaluateStepDisplayName(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateStepDisplayName(token, contextData, expressionFunctions);
        }

        public Dictionary<String, String> EvaluateStepEnvironment(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions,
            StringComparer keyComparer)
        {
            var legacyResult = _legacyEvaluator.EvaluateStepEnvironment(token, contextData, expressionFunctions, keyComparer);

            if (_compare)
            {
                try
                {
                    _context.Debug("Comparing new template evaluator: EvaluateStepEnvironment");
                    var convertedToken = ConvertToken(token);
                    var convertedData = ConvertData(contextData);
                    var convertedFunctions = ConvertFunctions(expressionFunctions);
                    var newResult = _newEvaluator.EvaluateStepEnvironment(convertedToken, convertedData, convertedFunctions, keyComparer);
                    if (!CompareStepEnvironment(legacyResult, newResult))
                    {
                        var telemetry = new JobTelemetry { Type = JobTelemetryType.General, Message = "TemplateEvaluatorMismatch: EvaluateStepEnvironment" };
                        _context.Global.JobTelemetry.Add(telemetry);
                    }
                }
                catch (Exception ex)
                {
                    _context.Debug($"Template evaluator comparison failed: {ex.Message}");
                    var telemetry = new JobTelemetry { Type = JobTelemetryType.General, Message = $"TemplateEvaluatorComparisonError: EvaluateStepEnvironment: {ex.Message}" };
                    _context.Global.JobTelemetry.Add(telemetry);
                }
            }

            return legacyResult;
        }

        public Boolean EvaluateStepIf(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions,
            IEnumerable<KeyValuePair<String, Object>> expressionState)
        {
            return _legacyEvaluator.EvaluateStepIf(token, contextData, expressionFunctions, expressionState);
        }

        public Dictionary<String, String> EvaluateStepInputs(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateStepInputs(token, contextData, expressionFunctions);
        }

        public Int32 EvaluateStepTimeout(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateStepTimeout(token, contextData, expressionFunctions);
        }

        public GitHub.DistributedTask.Pipelines.JobContainer EvaluateJobContainer(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            var legacyResult = _legacyEvaluator.EvaluateJobContainer(token, contextData, expressionFunctions);

            if (_compare)
            {
                try
                {
                    // TODO: Need to convert parameter types from DT types to Actions types:
                    // - TemplateToken: GitHub.DistributedTask.ObjectTemplating.Tokens.TemplateToken → GitHub.Actions.WorkflowParser.ObjectTemplating.Tokens.TemplateToken
                    // - DictionaryContextData: GitHub.DistributedTask.Pipelines.ContextData.DictionaryContextData → GitHub.Actions.Expressions.Data.DictionaryExpressionData
                    // - IFunctionInfo: GitHub.DistributedTask.Expressions2.IFunctionInfo → GitHub.Actions.Expressions.IFunctionInfo
                    // - Result type: GitHub.DistributedTask.Pipelines.JobContainer → GitHub.Actions.WorkflowParser.JobContainer
                    //
                    // var newResult = _newEvaluator.EvaluateJobContainer(convertedToken, convertedContextData, convertedFunctions);
                    // 
                    // // Compare results - use JSON serialization for deep comparison
                    // var legacyJson = StringUtil.ConvertToJson(legacyResult);
                    // var newJson = StringUtil.ConvertToJson(newResult);
                    // 
                    // if (legacyJson != newJson)
                    // {
                    //     var telemetry = new JobTelemetry { Type = "TemplateEvaluatorMismatch", Message = "EvaluateJobContainer" };
                    //     _context.Global.JobTelemetry.Add(telemetry);
                    // }
                }
                catch (Exception)
                {
                    // Silently ignore comparison errors
                }
            }

            return legacyResult;
        }

        public Dictionary<String, String> EvaluateJobOutput(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateJobOutput(token, contextData, expressionFunctions);
        }

        public TemplateToken EvaluateEnvironmentUrl(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateEnvironmentUrl(token, contextData, expressionFunctions);
        }

        public Dictionary<String, String> EvaluateJobDefaultsRun(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateJobDefaultsRun(token, contextData, expressionFunctions);
        }

        public IList<KeyValuePair<String, GitHub.DistributedTask.Pipelines.JobContainer>> EvaluateJobServiceContainers(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            var legacyResult = _legacyEvaluator.EvaluateJobServiceContainers(token, contextData, expressionFunctions);

            if (_compare)
            {
                try
                {
                    // TODO: Need to convert parameter types from DT types to Actions types:
                    // - TemplateToken: GitHub.DistributedTask.ObjectTemplating.Tokens.TemplateToken → GitHub.Actions.WorkflowParser.ObjectTemplating.Tokens.TemplateToken
                    // - DictionaryContextData: GitHub.DistributedTask.Pipelines.ContextData.DictionaryContextData → GitHub.Actions.Expressions.Data.DictionaryExpressionData
                    // - IFunctionInfo: GitHub.DistributedTask.Expressions2.IFunctionInfo → GitHub.Actions.Expressions.IFunctionInfo
                    // - Result type: GitHub.DistributedTask.Pipelines.JobContainer → GitHub.Actions.WorkflowParser.JobContainer
                    //
                    // var newResult = _newEvaluator.EvaluateJobServiceContainers(convertedToken, convertedContextData, convertedFunctions);
                    // 
                    // // Compare results - use JSON serialization for deep comparison
                    // var legacyJson = StringUtil.ConvertToJson(legacyResult);
                    // var newJson = StringUtil.ConvertToJson(newResult);
                    // 
                    // if (legacyJson != newJson)
                    // {
                    //     var telemetry = new JobTelemetry { Type = "TemplateEvaluatorMismatch", Message = "EvaluateJobServiceContainers" };
                    //     _context.Global.JobTelemetry.Add(telemetry);
                    // }
                }
                catch (Exception)
                {
                    // Silently ignore comparison errors
                }
            }

            return legacyResult;
        }

        public GitHub.DistributedTask.Pipelines.Snapshot EvaluateJobSnapshotRequest(
            TemplateToken token,
            DictionaryContextData contextData,
            IList<IFunctionInfo> expressionFunctions)
        {
            return _legacyEvaluator.EvaluateJobSnapshotRequest(token, contextData, expressionFunctions);
        }

        private GitHub.Actions.WorkflowParser.ObjectTemplating.Tokens.TemplateToken ConvertToken(
            GitHub.DistributedTask.ObjectTemplating.Tokens.TemplateToken token)
        {
            if (token == null)
            {
                return null;
            }

            var json = StringUtil.ConvertToJson(token, Newtonsoft.Json.Formatting.None);
            return StringUtil.ConvertFromJson<GitHub.Actions.WorkflowParser.ObjectTemplating.Tokens.TemplateToken>(json);
        }

        private GitHub.Actions.Expressions.Data.DictionaryExpressionData ConvertData(
            GitHub.DistributedTask.Pipelines.ContextData.DictionaryContextData contextData)
        {
            if (contextData == null)
            {
                return null;
            }

            var json = StringUtil.ConvertToJson(contextData, Newtonsoft.Json.Formatting.None);
            return StringUtil.ConvertFromJson<GitHub.Actions.Expressions.Data.DictionaryExpressionData>(json);
        }

        private IList<GitHub.Actions.Expressions.IFunctionInfo> ConvertFunctions(
            IList<GitHub.DistributedTask.Expressions2.IFunctionInfo> expressionFunctions)
        {
            if (expressionFunctions == null)
            {
                return null;
            }

            var result = new List<GitHub.Actions.Expressions.IFunctionInfo>();
            foreach (var func in expressionFunctions)
            {
                GitHub.Actions.Expressions.IFunctionInfo newFunc = func.Name switch
                {
                    "always" => new GitHub.Actions.Expressions.FunctionInfo<Expressions.NewAlwaysFunction>(func.Name, func.MinParameters, func.MaxParameters),
                    "cancelled" => new GitHub.Actions.Expressions.FunctionInfo<Expressions.NewCancelledFunction>(func.Name, func.MinParameters, func.MaxParameters),
                    "failure" => new GitHub.Actions.Expressions.FunctionInfo<Expressions.NewFailureFunction>(func.Name, func.MinParameters, func.MaxParameters),
                    "success" => new GitHub.Actions.Expressions.FunctionInfo<Expressions.NewSuccessFunction>(func.Name, func.MinParameters, func.MaxParameters),
                    "hashFiles" => new GitHub.Actions.Expressions.FunctionInfo<Expressions.NewHashFilesFunction>(func.Name, func.MinParameters, func.MaxParameters),
                    _ => throw new NotSupportedException($"Expression function '{func.Name}' is not supported for conversion")
                };
                result.Add(newFunc);
            }
            return result;
        }

        private bool CompareStepEnvironment(
            Dictionary<String, String> legacyResult,
            Dictionary<String, String> newResult)
        {
            if (legacyResult == null && newResult == null)
            {
                return true;
            }

            if (legacyResult == null || newResult == null)
            {
                _context.Debug($"EvaluateStepEnvironment mismatch: One result is null (legacy={legacyResult == null}, new={newResult == null})");
                return false;
            }

            if (legacyResult.Count != newResult.Count)
            {
                _context.Debug($"EvaluateStepEnvironment mismatch: Different counts (legacy={legacyResult.Count}, new={newResult.Count})");
                return false;
            }

            foreach (var kvp in legacyResult)
            {
                if (!newResult.TryGetValue(kvp.Key, out var newValue))
                {
                    _context.Debug($"EvaluateStepEnvironment mismatch: Key '{kvp.Key}' not found in new result");
                    return false;
                }

                if (kvp.Value != newValue)
                {
                    _context.Debug($"EvaluateStepEnvironment mismatch: Key '{kvp.Key}' has different values (legacy='{kvp.Value}', new='{newValue}')");
                    return false;
                }
            }

            return true;
        }
    }
}
