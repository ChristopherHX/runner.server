using System;
using System.Collections.Generic;
using System.Linq;
using GitHub.DistributedTask.ObjectTemplating.Tokens;
using GitHub.DistributedTask.Pipelines;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.WebApi;

namespace Runner.Server.Azure.Devops {

public class Pipeline {
    public string Name { get; set; }
    public List<Stage> Stages { get; set; }
    public Dictionary<string, VariableValue> Variables { get; set; }
    public Dictionary<string, Container> ContainerResources { get; set; }
    public Dictionary<string, TemplateToken> OtherResources { get; set; }
    public TemplateToken Trigger { get; set; }
    public TemplateToken Pr { get; set; }
    public TemplateToken Schedules { get; set; }
    public Pool Pool { get; set; }
    public bool? AppendCommitMessageToRunName { get; set; }
    public String LockBehavior { get; set; }

    public Pipeline Parse(Runner.Server.Azure.Devops.Context context, TemplateToken source) {
        var pipelineRootToken = source.AssertMapping("pipeline-root");
        Pipeline parent = null;
        foreach(var kv in pipelineRootToken) {
            switch(kv.Key.AssertString("key").Value) {
                case "name":
                    Name = kv.Value.AssertLiteralString("name");
                break;
                case "variables":
                    Variables = new Dictionary<string, VariableValue>(StringComparer.OrdinalIgnoreCase);
                    AzureDevops.ParseVariables(context, Variables, kv.Value);
                break;
                case "extends":
                    var ext = kv.Value.AssertMapping("extends");
                    string template = null;
                    Dictionary<string, TemplateToken> parameters = null;
                    foreach(var ev in ext) {
                        switch(ev.Key.AssertString("").Value) {
                            case "template":
                                template = ev.Value.AssertString("").Value;
                            break;
                            case "parameters":
                                parameters = ev.Value.AssertMapping("param").ToDictionary(pv => pv.Key.AssertString("").Value, pv => pv.Value);
                            break;
                        }
                    }
                    var templ = AzureDevops.ReadTemplate(context, template, parameters);
                    parent = new Pipeline().Parse(context.ChildContext(templ, template), templ);
                break;
                case "stages":
                    Stages = new List<Stage>();
                    Stage.ParseStages(context, Stages, kv.Value.AssertSequence("stages"));
                break;
                case "steps":
                    var implicitJob = new Job().Parse(context, source);
                    implicitJob.Name = null;
                    Stages = new List<Stage>{ new Stage {
                        Jobs = new List<Job>{ implicitJob }
                    } };
                break;
                case "jobs":
                    var implicitStage = new Stage().Parse(context, source);
                    implicitStage.Name = null;
                    Stages = new List<Stage>{ implicitStage };
                break;
                case "resources":
                    foreach(var resource in kv.Value.AssertMapping("resources")) {
                        switch(resource.Key.AssertString("").Value) {
                            case "containers":
                                ContainerResources = new Dictionary<string, Container>(StringComparer.OrdinalIgnoreCase);
                                foreach(var rawcontainer in resource.Value.AssertSequence("cres")) {
                                    var container = rawcontainer.AssertMapping("");
                                    ContainerResources[container[0].Value.AssertString("").Value] = new Container().Parse(container);
                                }
                            break;
                            default:
                                if(OtherResources == null) {
                                    OtherResources = new Dictionary<string, TemplateToken>(StringComparer.OrdinalIgnoreCase);
                                }
                                OtherResources[resource.Key.ToString()] = resource.Value;
                            break;
                        } 
                    }
                break;
                case "pool":
                    Pool = new Pool().Parse(context, kv.Value);
                break;
                case "appendCommitMessageToRunName":
                    AppendCommitMessageToRunName = kv.Value.AssertAzurePipelinesBoolean("appendCommitMessageToRunName have to be of type bool");
                break;
                case "lockBehavior":
                    LockBehavior = kv.Value.AssertLiteralString("lockBehavior have to be of type string");
                break;
                case "trigger":
                    Trigger = kv.Value;
                break;
                case "pr":
                    Pr = kv.Value;
                break;
                case "schedules":
                    Schedules = kv.Value;
                break;
            }
        }
        if(parent != null) {
            Stages = parent.Stages;
            if(parent.ContainerResources != null) {
                foreach(var cr in parent.ContainerResources) {
                    ContainerResources[cr.Key] = cr.Value;
                }
            }
            if(parent.Variables != null) {
                foreach(var cr in parent.Variables) {
                    Variables[cr.Key] = cr.Value;
                }
            }
        }
        return this;
    }

    public DictionaryContextData ToContextData() {
        var pipeline = new DictionaryContextData();
        if(!string.IsNullOrEmpty(Name)) {
            pipeline["name"] = new StringContextData(Name);
        }
        if(Variables != null) {
            var vars = new DictionaryContextData();
            foreach(var v in Variables) {
                vars[v.Key] = new StringContextData(v.Value.Value);
            }
            pipeline["variables"] = vars;
        }
        if(ContainerResources != null || OtherResources != null) {
            var resources = new DictionaryContextData();
            pipeline["resources"] = resources;
            if(ContainerResources != null) {
                var containers = new ArrayContextData();
                resources["containers"] = containers;
                foreach(var cr in ContainerResources) {
                    containers.Add(cr.Value.ToContextData(cr.Key));
                }
            }
            if(OtherResources != null) {
                foreach(var ores in OtherResources) {
                    resources[ores.Key] = ores.Value.ToContextData();
                }
            }
        }
        if(Stages != null) {
            var stageList = new ArrayContextData();
            foreach(var stage in Stages) {
                stageList.Add(stage.ToContextData());
            }
            pipeline["stages"] = stageList;
        }
        if(Pool != null) {
            pipeline["pool"] = Pool.ToContextData();
        }
        if(AppendCommitMessageToRunName != null) {
            pipeline["appendCommitMessageToRunName"] = new BooleanContextData(AppendCommitMessageToRunName.Value);
        }
        if(LockBehavior != null) {
            pipeline["lockBehavior"] = new StringContextData(LockBehavior);
        }
        if(Trigger != null) {
            pipeline["trigger"] = Trigger.ToContextData();
        }
        if(Pr != null) {
            pipeline["pr"] = Pr.ToContextData();
        }
        if(Schedules != null) {
            pipeline["schedules"] = Schedules.ToContextData();
        }
        
        return pipeline;
    }
}
}