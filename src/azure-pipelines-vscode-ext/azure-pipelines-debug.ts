import {
	Logger, logger,
	LoggingDebugSession,
	InitializedEvent, TerminatedEvent
} from '@vscode/debugadapter';
import { DebugProtocol } from '@vscode/debugprotocol';

import * as vscode from 'vscode'

interface ILaunchRequestArguments extends DebugProtocol.LaunchRequestArguments {
	program: string;
	trace?: boolean;
	watch?: boolean;
	preview?: boolean;
	repositories?: string[];
	variables?: string[];
	parameters?: string[];
}

interface IAttachRequestArguments extends ILaunchRequestArguments { }


export class AzurePipelinesDebugSession extends LoggingDebugSession {
    watcher: vscode.FileSystemWatcher
    virtualFiles: any
    name: string
    expandAzurePipeline: any
    changed: any
	disposables: vscode.Disposable[]

	public constructor(virtualFiles: any, name: string, expandAzurePipeline: any, changed: any) {
		super("azure-pipelines-debug.yml");
		this.setDebuggerLinesStartAt1(false);
		this.setDebuggerColumnsStartAt1(false);
        this.virtualFiles = virtualFiles;
        this.name = name;
        this.expandAzurePipeline = expandAzurePipeline;
        this.changed = changed;
		this.disposables = [];
	}

	protected initializeRequest(response: DebugProtocol.InitializeResponse, args: DebugProtocol.InitializeRequestArguments): void {
		response.body = response.body || {};
		response.body.supportsConfigurationDoneRequest = true;
		response.body.supportsEvaluateForHovers = false;
		response.body.supportsStepBack = false;
		response.body.supportsDataBreakpoints = false;
		response.body.supportsCompletionsRequest = false;
		response.body.completionTriggerCharacters = [ ".", "[" ];
		response.body.supportsCancelRequest = false;
		response.body.supportsBreakpointLocationsRequest = false;
		response.body.supportsStepInTargetsRequest = false;
		response.body.supportsExceptionFilterOptions = false;
		response.body.exceptionBreakpointFilters = [];
		response.body.supportsExceptionInfoRequest = false;
		response.body.supportsSetVariable = false;
		response.body.supportsSetExpression = false;
		response.body.supportsDisassembleRequest = false;
		response.body.supportsSteppingGranularity = false;
		response.body.supportsInstructionBreakpoints = false;
		response.body.supportsReadMemoryRequest = false;
		response.body.supportsWriteMemoryRequest = false;
		response.body.supportSuspendDebuggee = false;
		response.body.supportTerminateDebuggee = true;
		response.body.supportsFunctionBreakpoints = false;
		response.body.supportsDelayedStackTraceLoading = false;

		this.sendResponse(response);
		this.sendEvent(new InitializedEvent());
	}

	protected async attachRequest(response: DebugProtocol.AttachResponse, args: IAttachRequestArguments) {
		return this.launchRequest(response, args);
	}

	protected async launchRequest(response: DebugProtocol.LaunchResponse, args: ILaunchRequestArguments) {
		logger.setup(args.trace ? Logger.LogLevel.Verbose : Logger.LogLevel.Stop, false);

		var self = this;
		var message = null;
		var requestReOpen = false;
		if(args.preview) {
			self.virtualFiles[self.name] = "";
			var uri = vscode.Uri.from({
				scheme: "azure-pipelines-vscode-ext",
				path: this.name
			});
			var doc = await vscode.workspace.openTextDocument(uri);
			await vscode.window.showTextDocument(doc, { preview: true, viewColumn: vscode.ViewColumn.Two, preserveFocus: true });
			this.disposables.push(vscode.window.onDidChangeVisibleTextEditors(textEditors => {
				if(!requestReOpen && !vscode.window.tabGroups.all.some(g => g.tabs.some(t => t.input["uri"] && t.input["uri"].toString() === uri.toString()))) {
					console.log(`file closed ${self.name}`);
					requestReOpen = true;
				}
			}));
			this.disposables.push(vscode.workspace.onDidCloseTextDocument(adoc => {
				if(doc === adoc) {
					delete self.virtualFiles[self.name];
				}
			}));
			self.changed(uri);
		}
		var run = async() => {
			await this.expandAzurePipeline(false, args.repositories, args.variables, args.parameters, async result => {
				if(args.preview) {
					if(requestReOpen) {
						await vscode.window.showTextDocument(doc, { preview: true, viewColumn: vscode.ViewColumn.Two, preserveFocus: true });
					}
					self.virtualFiles[self.name] = result;
					self.changed(uri);
				} else {
					vscode.window.showInformationMessage("No Issues found");
				}
			}, args.program, async errmsg => {
				if(args.preview) {
					if(requestReOpen) {
						await vscode.window.showTextDocument(doc, { preview: true, viewColumn: vscode.ViewColumn.Two, preserveFocus: true });
					}
					self.virtualFiles[self.name] = errmsg;
					self.changed(uri);
				} else if(args.watch) {
					vscode.window.showErrorMessage(errmsg);
				} else {
					message = errmsg;
				}
			});
		};
		try {
			await run();
		} catch(ex) {
			console.log(ex?.toString() ?? "<??? error>");
		}
		if(args.watch) {
			this.watcher = vscode.workspace.createFileSystemWatcher("**/*.{yml,yaml}");
			this.watcher.onDidCreate(e => {
				console.log(`created: ${e.toString()}`);
				run();
			});
			this.watcher.onDidChange(e => {
				console.log(`changed: ${e.toString()}`);
				run();
			});
			this.watcher.onDidDelete(e => {
				console.log(`deleted: ${e.toString()}`);
				run();
			});
		} else {
			if(message) {
				this.sendErrorResponse(response, {
					id: 1001,
					format: message,
					showUser: true
				});
			} else {
				this.sendResponse(response);
				this.sendEvent(new TerminatedEvent());
			}
		}
	}

	protected configurationDoneRequest(response: DebugProtocol.ConfigurationDoneResponse, args: DebugProtocol.ConfigurationDoneArguments, request?: DebugProtocol.Request): void {
		this.sendResponse(response);
	}

    protected disconnectRequest(response: DebugProtocol.DisconnectResponse, args: DebugProtocol.DisconnectArguments, request?: DebugProtocol.Request): void {
		console.log(`disconnectRequest suspend: ${args.suspendDebuggee}, terminate: ${args.terminateDebuggee}`);
        if (this.watcher) {
            this.watcher.dispose();
        }
		if (this.disposables) {
			for(const disposable of this.disposables) {
				disposable.dispose();
			}
		}
		delete this.virtualFiles[self.name];
		this.sendResponse(response);
	}
}
