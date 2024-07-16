const vscode = require('vscode');
import { basePaths, customImports } from "./config.js"
import { AzurePipelinesDebugSession } from "./azure-pipelines-debug";
import { integer } from "vscode-languageclient";

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
	basePaths.basedir = context.extensionUri.with({ path: context.extensionUri.path + "/build/AppBundle/_framework/blazor.boot.json" }).toString();
	// var dotnet = null;
	var { dotnet } = require("./build/AppBundle/_framework/dotnet.js");
	customImports["dotnet.runtime.js"] = require("./build/AppBundle/_framework/dotnet.runtime.js");
	customImports["dotnet.native.js"] = require("./build/AppBundle/_framework/dotnet.native.js");

	var logchannel = vscode.window.createOutputChannel("Azure Pipeline Evaluation Log", { log: true });

	var virtualFiles = {};
	var myScheme = "azure-pipelines-vscode-ext";
	var changeDoc = new vscode.EventEmitter();
	vscode.workspace.registerTextDocumentContentProvider(myScheme, {
		onDidChange: changeDoc.event,
		provideTextDocumentContent(uri) {
			return virtualFiles[uri.path];
		}
	});
	var joinPath = (l, r) => l ? l + "/" + r : r;
	var loadingPromise = null;
	var runtimePromise = () => loadingPromise ??= vscode.window.withProgress({
		location: vscode.ProgressLocation.Notification,
		title: "Updating Runtime",
		cancellable: true
	}, async (progress, token) => {
		logchannel.appendLine("Updating Runtime");
		// var res = await import("./build/AppBundle/_framework/dotnet.js");
		// dotnet = res.dotnet;
		// customImports["dotnet.runtime.js"] = await import("./build/AppBundle/_framework/dotnet.runtime.js");
		// customImports["dotnet.native.js"] = await import("./build/AppBundle/_framework/dotnet.native.js");
		var items = 1;
		var citem = 0;
		var runtime = await dotnet.withOnConfigLoaded(async config => {
			items = Object.keys(config.resources.assembly).length;
		}).withConfigSrc(context.extensionUri.with({ path: context.extensionUri.path + "/build/AppBundle/_framework/blazor.boot.json" }).toString()).withResourceLoader((type, name, defaultUri, integrity, behavior) => {
			if(type === "dotnetjs") {
				// Allow both nodejs and browser to use the same code
				customImports[defaultUri] = customImports[name];
				return defaultUri;
			}
			return (async () => {
				if(token.isCancellationRequested) {
					throw new Error("loading Runtime aborted, reload the window to use this extension");
				}
				if(type === "assembly") {
					await progress.report({ message: name, increment: citem++ / items });
				}
				if(type == "globalization" && name.endsWith(".dat")) {
					name = name.substring(name.length - 3) + "icu";
				}
				var content = await vscode.workspace.fs.readFile(context.extensionUri.with({ path: context.extensionUri.path + "/build/AppBundle/_framework/" + name }));
				return new Response(content, { status: 200 });
			})();
		}).create();
		runtime.setModuleImports("extension.js", {
			readFile: async (handle, repositoryAndRef, filename) => {
				try {
					var uri = "";
					if(repositoryAndRef) {
						if(handle.repositories && repositoryAndRef in handle.repositories) {
							var base = vscode.Uri.parse(handle.repositories[repositoryAndRef]);
							uri = base.with({ path: joinPath(base.path, filename) });
						} else {
							var result = handle.askForInput ? await vscode.window.showInputBox({
								ignoreFocusOut: true,
								placeHolder: "value",
								prompt: `${repositoryAndRef} (${filename})`,
								title: "Provide the uri to the required Repository"
							}) : null;
							if(result) {
								handle.repositories ??= {};
								handle.repositories[repositoryAndRef] = result;
								var base = vscode.Uri.parse(result);
								uri = base.with({ path: joinPath(base.path, filename) });
							} else {
								logchannel.error(`Cannot access remote repository ${repositoryAndRef} (${filename})`);
								return null;
							}
						}
					} else {
						// Get current textEditor content for the entrypoint
						// var doc = handle.textEditor ? handle.textEditor.document : null;
						// if(handle.filename === filename && doc && !handle.skipCurrentEditor) {
						// 	handle.referencedFiles.push(handle.textEditor.document.uri);
						// 	handle.refToUri[`(${repositoryAndRef ?? "self"})/${filename}`] = handle.textEditor.document.uri;
						// 	return doc.getText();
						// }
						uri = handle.base.with({ path: joinPath(handle.base.path, filename) });
					}
					handle.referencedFiles.push(uri);
					handle.refToUri[`(${repositoryAndRef ?? "self"})/${filename}`] = uri;
					var scontent = null;
					var textDocument = vscode.workspace.textDocuments.find(t => t.uri.toString() === uri.toString());
					if(textDocument) {
						scontent = textDocument.getText();
					} else {
						// Read template references via filesystem api
						var content = await vscode.workspace.fs.readFile(uri);
						scontent = new TextDecoder().decode(content);
					}
					return scontent;
				} catch(ex) {
					logchannel.error(`Failed to access ${filename} (${repositoryAndRef ?? "self"}), error: ${ex.toString()}`);
					return null;
				}
			},
			message: async (handle, type, content) => {
				switch(type) {
					case 0:
						((handle.task && handle.task.info) ?? logchannel.info)(content);
						await vscode.window.showInformationMessage(content);
						break;
					case 1:
						((handle.task && handle.task.warn) ?? logchannel.warn)(content);
						await vscode.window.showWarningMessage(content);
						break;
					case 2:
						((handle.task && handle.task.error) ?? logchannel.error)(content);
						await vscode.window.showErrorMessage(content);
						break;
				}
			},
			sleep: time => new Promise((resolve, reject) => setTimeout(resolve), time),
			log: (handle, type, message) => {
				switch(type) {
					case 1:
						((handle.task && handle.task.trace) ?? logchannel.trace)(message);
						break;
					case 2:
						((handle.task && handle.task.debug) ?? logchannel.debug)(message);
						break;
					case 3:
						((handle.task && handle.task.info) ?? logchannel.info)(message);
						break;
					case 4:
						((handle.task && handle.task.warn) ?? logchannel.warn)(message);
						break;
					case 5:
						((handle.task && handle.task.error) ?? logchannel.error)(message);
						break;
				}
			},
			requestRequiredParameter: async (handle, name) => {
				if(!handle.askForInput) {
					return;
				}
				return handle.parameters[name] = await vscode.window.showInputBox({
					ignoreFocusOut: true,
					placeHolder: "value",
					prompt: name,
					title: "Provide required Variables in yaml notation"
				})
			},
			error: async (handle, message) => {
				await handle.error(message);
			}
		});
		logchannel.appendLine("Starting extension main to keep dotnet alive");
		runtime.runMainAndExit("ext-core", []);
		logchannel.appendLine("Runtime is now ready");
		return runtime;
	}).catch(async ex => {
		// Failed to load, allow retry
		loadingPromise = null;
		await vscode.window.showErrorMessage("Failed to load .net: " + ex.toString());
	});

	var defcollection = vscode.languages.createDiagnosticCollection();
	var expandAzurePipeline = async (validate, repos, vars, params, callback, fspathname, error, task, collection, state, skipAskForInput) => {
		collection ??= defcollection;
		var textEditor = vscode.window.activeTextEditor;
		if(!textEditor && !fspathname) {
			await vscode.window.showErrorMessage("No active TextEditor");
			return;
		}
		var oldConf = vscode.workspace.getConfiguration("azure-pipelines");
		var conf = vscode.workspace.getConfiguration("azure-pipelines-vscode-ext");
		var repositories = {};
		for(var repo of [...(oldConf.repositories ?? []), ...(conf.repositories ?? [])]) {
			var line = repo.split("=");
			var name = line.shift();
			repositories[name] = line.join("=");
		}
		if(repos) {
			for(var name in repos) {
				repositories[name] = repos[name];
			}
		}
		var variables = {};
		for(var repo of [...(oldConf.variables ?? []), ...(conf.variables ?? [])]) {
			var line = repo.split("=");
			var name = line.shift();
			variables[name] = line.join("=");
		}
		if(vars) {
			for(var name in vars) {
				variables[name] = vars[name];
			}
		}
		var parameters = {};
		if(params) {
			for(var name in params) {
				parameters[name] = JSON.stringify(params[name]);
			}
		} else {
			for(var repo of [...(oldConf.parameters ?? []), ...(conf.parameters ?? [])]) {
				var line = repo.split("=");
				var name = line.shift();
				parameters[name] = line.join("=");
			}
		}

		var runtime = await runtimePromise();
		var base = null;

		var skipCurrentEditor = false;
		var filename = null
		if(fspathname) {
			skipCurrentEditor = true;
			var uris = [vscode.Uri.parse(fspathname), vscode.Uri.file(fspathname)];
			for(var current of uris) {
				var rbase = vscode.workspace.getWorkspaceFolder(current);
				var name = vscode.workspace.asRelativePath(current, false);
				if(rbase && name) {
					base = rbase.uri;
					filename = name;
					break;
				}
			}
			if(filename == null) {
				for(var workspace of (vscode.workspace.workspaceFolders || [])) {
					// Normalize
					var nativepathname = vscode.Uri.file(fspathname).fsPath;
					if(nativepathname.startsWith(workspace.uri.fsPath)) {
						base = workspace.uri;
						filename = vscode.workspace.asRelativePath(workspace.uri.with({path: joinPath(workspace.uri.path, nativepathname.substring(workspace.uri.fsPath.length).replace(/[\\\/]+/g, "/"))}), false);
						break;
					}
				}
			}
			if(filename == null) {
				// untitled uris will land here
				var current = vscode.Uri.parse(fspathname);
				for(var workspace of (vscode.workspace.workspaceFolders || [])) {
					var workspacePath = workspace.uri.path.replace(/\/*$/, "/");
					if(workspace.uri.scheme === current.scheme && workspace.uri.authority === current.authority && current.path.startsWith(workspacePath)) {
						base = workspace.uri;
						filename = current.path.substring(workspacePath.length);
						break;
					}
				}
				var li = current.path.lastIndexOf("/");
				base ??= current.with({ path: current.path.substring(0, li)});
				filename ??= current.path.substring(li + 1);
				try {
					await vscode.workspace.fs.stat(current);
				} catch {
					// untitled uris cannot be read by readFile
					skipCurrentEditor = false;
				}
			}
		} else {
			filename = null;
			var current = textEditor.document.uri;
			for(var workspace of (vscode.workspace.workspaceFolders || [])) {
				var workspacePath = workspace.uri.path.replace(/\/*$/, "/");
				if(workspace.uri.scheme === current.scheme && workspace.uri.authority === current.authority && current.path.startsWith(workspacePath)) {
					base = workspace.uri;
					filename = current.path.substring(workspacePath.length);
					break;
				}
			}
			var li = current.path.lastIndexOf("/");
			base ??= current.with({ path: current.path.substring(0, li)});
			filename ??= current.path.substring(li + 1);
		}
		var handle = { hasErrors: false, base: base, skipCurrentEditor: skipCurrentEditor, textEditor: textEditor, filename: filename, repositories: repositories, parameters: parameters, error: (async jsonex => {
			var items = [];
			var pex = JSON.parse(jsonex);
			for(var ex of pex.Errors) {
				var matched = false;
				var err = null;
				let i = ex.indexOf(" (Line: ");
				if(i !== -1 && !matched) {
					let m = ex.substring(i).match(/^ \(Line: (\d+), Col: (\d+)\): (.*)$/);
					if(m) {
						err = [m.shift(), ex.substring(0, i), ...m];
					}
				}
				if(err) {
					var ref = err[1];
					var row = parseInt(err[2]) - 1;
					var column = parseInt(err[3]) - 1;
					var msg = err[4];
					var range = new vscode.Range(new vscode.Position(row, column), new vscode.Position(row, integer.MAX_VALUE));
					var diag = new vscode.Diagnostic(range, msg, vscode.DiagnosticSeverity.Error);
					var uri = handle.refToUri[ref];
					if(uri) {
						matched = true;
						items.push([uri, [diag]]);
					}
				}
				err = null;
				if(i !== -1 && !matched) {
					let m = ex.substring(i - 1).match(/^: \(Line: (\d+), Col: (\d+), Idx: \d+\) - \(Line: (\d+), Col: (\d+), Idx: \d+\): (.*)$/);
					if(m) {
						err = [m.shift(), ex.substring(0, i - 1), ...m];
					}
				}
				if(err) {
					var ref = err[1];
					var row = parseInt(err[2]) - 1;
					var column = parseInt(err[3]) - 1;
					var rowEnd = parseInt(err[4]) - 1;
					var columnEnd = parseInt(err[5]) - 1;
					var msg = err[6];
					var range = new vscode.Range(new vscode.Position(row, column), new vscode.Position(rowEnd, columnEnd));
					var diag = new vscode.Diagnostic(range, msg, vscode.DiagnosticSeverity.Error);
					var uri = handle.refToUri[ref];
					if(uri) {
						matched = true;
						items.push([uri, [diag]]);
					}
				}
				err = !matched ? ex.match(/^([^:]+): (.*)$/) : null;
				if(err) {
					var ref = err[1];
					var msg = err[2];
					var range = new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
					var diag = new vscode.Diagnostic(range, msg, vscode.DiagnosticSeverity.Error);
					var uri = handle.refToUri[ref];
					if(uri) {
						matched = true;
						items.push([uri, [diag]]);
					}
				}
				if(!matched) {
					var uri = handle.refToUri[`(self)/${handle.filename}`];
					var range = new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0));
					var diag = new vscode.Diagnostic(range, ex, vscode.DiagnosticSeverity.Error);
					if(uri) {
						items.push([uri, [diag]]);
					}
				}
			}
			for(var uri of handle.referencedFiles) {
				if(uri) {
					items.push([uri, []]);
				}
			}
			handle.hasErrors = true;
			collection.set(items);
			if(!error) {
				await vscode.window.showErrorMessage(pex.Message);
				return;
			}
			return await error(pex.Message);
		}), referencedFiles: [], refToUri: {}, task: task, askForInput: !skipAskForInput };
		var result = await runtime.BINDING.bind_static_method("[ext-core] MyClass:ExpandCurrentPipeline")(handle, filename, JSON.stringify(variables), JSON.stringify(parameters), (error && true) == true);

        if(state) {
            state.referencedFiles = handle.referencedFiles;
			if(!skipAskForInput) {
				state.repositories = handle.repositories;
				var rawparams = {};
				var binding = runtime.BINDING.bind_static_method("[ext-core] MyClass:YAMLToJson");
				for(var name in handle.parameters) {
					try {
						var yml = handle.parameters[name];
						var js = binding(yml);
						rawparams[name] = JSON.parse(js);
					} catch(ex) {
						console.log(ex);
					}
				}
				state.parameters = rawparams;
			}
        }

		if(result) {
			logchannel.debug(result);
			if(!handle.hasErrors) {
				var items = [];
				for(var uri of handle.referencedFiles) {
					items.push([uri, []]);
				}
				collection.set(items);
			}
			if(validate) {
				if(!handle.hasErrors) {
					await vscode.window.showInformationMessage("No issues found");
				}
			} else if(callback) {
				callback(result);
			} else {
				await vscode.workspace.openTextDocument({ language: "yaml", content: result });
			}
		}
	};

	var expandAzurePipelineCommand = () => {
		vscode.tasks.executeTask(
			new vscode.Task({
					type: "azure-pipelines-vscode-ext",
					program: vscode.window.activeTextEditor?.document?.uri?.toString(),
					watch: true,
					preview: true,
					autoClosePreview: true
				},
				vscode.TaskScope.Workspace,
				"Azure Pipeline Preview (watch)",
				"azure-pipelines",
				executor,
				null
			)
		);
	}

	var validateAzurePipelineCommand = () => {
		vscode.tasks.executeTask(
			new vscode.Task({
					type: "azure-pipelines-vscode-ext",
					program: vscode.window.activeTextEditor?.document?.uri?.toString(),
					watch: true,
				},
				vscode.TaskScope.Workspace,
				"Azure Pipeline Validate (watch)",
				"azure-pipelines",
				executor,
				null
			)
		);
	}

	context.subscriptions.push(vscode.commands.registerCommand('azure-pipelines-vscode-ext.expandAzurePipeline', () => expandAzurePipelineCommand()));

	context.subscriptions.push(vscode.commands.registerCommand('azure-pipelines-vscode-ext.validateAzurePipeline', () => validateAzurePipelineCommand()));

	context.subscriptions.push(vscode.commands.registerCommand('extension.expandAzurePipeline', () => expandAzurePipelineCommand()));

	context.subscriptions.push(vscode.commands.registerCommand('extension.validateAzurePipeline', () => validateAzurePipelineCommand()));

	var statusbar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right);

	statusbar.text = "Validate Azure Pipeline";

	statusbar.command = 'azure-pipelines-vscode-ext.validateAzurePipeline';

	var onLanguageChanged = languageId => {
		if(languageId === "azure-pipelines" || languageId === "yaml") {
			statusbar.show();
		} else {
			statusbar.hide();
		}
	};
	var z = 0;
	vscode.debug.registerDebugAdapterDescriptorFactory("azure-pipelines-vscode-ext", {
		createDebugAdapterDescriptor: (session, executable) => {
			return new vscode.DebugAdapterInlineImplementation(new AzurePipelinesDebugSession(virtualFiles, `azure-pipelines-preview-${z++}.yml`, expandAzurePipeline, arg => changeDoc.fire(arg)));
		}
	});

	var onTextEditChanged = texteditor => onLanguageChanged(texteditor && texteditor.document && texteditor.document.languageId ? texteditor.document.languageId : null);
	context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(onTextEditChanged))
	context.subscriptions.push(vscode.workspace.onDidCloseTextDocument(document => onLanguageChanged(document && document.languageId ? document.languageId : null)));
	context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(document => onLanguageChanged(document && document.languageId ? document.languageId : null)));
	onTextEditChanged(vscode.window.activeTextEditor);
	var executor = new vscode.CustomExecution(async def => {
		const writeEmitter = new vscode.EventEmitter();
		const closeEmitter = new vscode.EventEmitter();
		var self = {
			virtualFiles: virtualFiles,
			name: `azure-pipelines-preview-${z++}.yml`,
			changed: arg => changeDoc.fire(arg),
			disposables: [],
			parameters: null,
			repositories: null
		};
		self.collection = vscode.languages.createDiagnosticCollection(self.name);
		self.disposables.push(self.collection);
		var task = {
			trace: message => writeEmitter.fire("\x1b[34m[trace]" + message.replace(/\r?\n/g, "\r\n") + "\x1b[0m\r\n"),
			debug: message => writeEmitter.fire("\x1b[35m[debug]" + message.replace(/\r?\n/g, "\r\n") + "\x1b[0m\r\n"),
			info: message => writeEmitter.fire("\x1b[32m[info]" + message.replace(/\r?\n/g, "\r\n") + "\x1b[0m\r\n"),
			warn: message => writeEmitter.fire("\x1b[33m[warn]" + message.replace(/\r?\n/g, "\r\n") + "\x1b[0m\r\n"),
			error: message => writeEmitter.fire("\x1b[31m[error]" + message.replace(/\r?\n/g, "\r\n") + "\x1b[0m\r\n"),
		};
		var requestReOpen = true;
		var documentClosed = true;
		var assumeIsOpen = false;
		var doc = null;
		var uri = vscode.Uri.from({
			scheme: "azure-pipelines-vscode-ext",
			path: self.name
		});
		var reopenPreviewIfNeeded = async () => {
			if(requestReOpen) {
				if(documentClosed || !doc || doc.isClosed) {
					self.virtualFiles[self.name] = "";
					doc = await vscode.workspace.openTextDocument(uri);
				}
				await vscode.window.showTextDocument(doc, { preview: true, viewColumn: vscode.ViewColumn.Two, preserveFocus: true });
				requestReOpen = false;
				documentClosed = false;
			}
		};
		var close = () => {
			console.log("closed");
			writeEmitter.dispose();
			closeEmitter.fire(0);
			closeEmitter.dispose();
			if(self.watcher) {
				self.watcher.dispose();
			}
			if(self.disposables) {
				for(var disp of self.disposables) {
					disp.dispose();
				}
			}
		}
		return {
			close: close,
			onDidWrite: writeEmitter.event,
			onDidClose: closeEmitter.event,
			open: () => {
				var args = def;
				if(args.preview) {
					var previewIsOpen = () => vscode.window.tabGroups && vscode.window.tabGroups.all && vscode.window.tabGroups.all.some(g => g && g.tabs && g.tabs.some(t => t && t.input && t.input["uri"] && t.input["uri"].toString() === uri.toString()));
					self.disposables.push(vscode.window.tabGroups.onDidChangeTabs(e => {
						if(!previewIsOpen()) {
							if(!requestReOpen) {
								console.log(`file closed ${self.name}`);
							}
							requestReOpen = true;
						} else {
							if(requestReOpen) {
								console.log(`file opened ${self.name}`);
							}
							requestReOpen = false;
						}
					}));
					self.disposables.push(vscode.workspace.onDidCloseTextDocument(adoc => {
						if(doc === adoc) {
							if(args.autoClosePreview) {
								close();
								return;
							}
							delete self.virtualFiles[self.name];
							console.log(`document closed ${self.name}`);
							documentClosed = true;
						}
					}));
				}

				var inProgress = false;
				var waiting = false;
				var run = async (askForInput) => {
					if(inProgress) {
						waiting = true;
						return;
					}
					waiting = false;
					inProgress = true;
					try {
						var hasErrors = false;
						await expandAzurePipeline(false, self.repositories ?? args.repositories, args.variables, self.parameters ?? args.parameters, async result => {
							task.info(result);
							if(args.preview) {
								await reopenPreviewIfNeeded();
								self.virtualFiles[self.name] = result;
								self.changed(uri);
							} else if(!hasErrors) {
								vscode.window.showInformationMessage("No Issues found");
							}
						}, args.program, async errmsg => {
							hasErrors = true;
							task.error(errmsg);
							if(args.preview) {
								await reopenPreviewIfNeeded();
								self.virtualFiles[self.name] = errmsg;
								self.changed(uri);
							} else {
								vscode.window.showErrorMessage(errmsg);
							}
						}, task, self.collection, self, !askForInput);
					} catch {

					}
					inProgress = false;
					if(!args.watch) {
						close();
					}
					if(waiting) {
						run();
					}
				};
				run(true);
				if(def.watch) {
					var isReferenced = uri => self.referencedFiles.find((u) => u.toString() === uri.toString());
					// Reload yaml on file and textdocument changes
					self.disposables.push(vscode.workspace.onDidChangeTextDocument(ch => {
						var doc = ch.document;
						if(isReferenced(doc.uri)) {
							console.log(`changed(doc): ${doc.uri.toString()}`);
							run();
						}
					}));
					
					self.watcher = vscode.workspace.createFileSystemWatcher("**/*.{yml,yaml}");
					self.watcher.onDidCreate(e => {
						if(isReferenced(e)) {
							console.log(`created: ${e.toString()}`);
							run();
						}
					});
					self.watcher.onDidChange(e => {
						if(isReferenced(e) && !vscode.workspace.textDocuments.find(t => t.uri.toString() === e.toString())) {
							console.log(`changed: ${e.toString()}`);
							run();
						}
					});
					self.watcher.onDidDelete(e => {
						if(isReferenced(e)) {
							console.log(`deleted: ${e.toString()}`);
							run();
						}
					});
				}
			}
		}
	});
	context.subscriptions.push(vscode.tasks.registerTaskProvider("azure-pipelines-vscode-ext", {
		provideTasks: async token => ([
			new vscode.Task({
					type: "azure-pipelines-vscode-ext",
					variables: {},
					watch: true,
					preview: true
				},
				vscode.TaskScope.Workspace,
				"Azure Pipeline Preview (watch)",
				"azure-pipelines",
				executor,
				null
			)
		]),
		resolveTask: _task => {
			  // resolveTask requires that the same definition object be used.
			  	return new vscode.Task(_task.definition,
					vscode.TaskScope.Workspace,
					"Azure Pipeline Preview (watch)",
					"azure-pipelines",
					executor,
					null
				);
		}
	}));
}

// this method is called when your extension is deactivated
function deactivate() {}

// eslint-disable-next-line no-undef
export {
	activate,
	deactivate
}
