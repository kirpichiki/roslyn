// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell.Interop;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem
{
    internal partial class AbstractProject
    {
        public void AddAnalyzerReference(string analyzerAssemblyFullPath)
        {
            AssertIsForeground();

            // TODO: implement
            /*

            if (CurrentProjectAnalyzersContains(analyzerAssemblyFullPath))
            {
                return;
            }

            var fileChangeService = (IVsFileChangeEx)this.ServiceProvider.GetService(typeof(SVsFileChangeEx));
            if (Workspace == null)
            {
                // This can happen only in tests.
                var testAnalyzer = new VisualStudioAnalyzer(analyzerAssemblyFullPath, fileChangeService, this.HostDiagnosticUpdateSource, this.Id, this.Workspace, loader: null, language: this.Language);
                this.AddOrUpdateAnalyzer(analyzerAssemblyFullPath, testAnalyzer);
                return;
            }

            var analyzerLoader = Workspace.Services.GetRequiredService<IAnalyzerService>().GetLoader();
            analyzerLoader.AddDependencyLocation(analyzerAssemblyFullPath);
            var analyzer = new VisualStudioAnalyzer(analyzerAssemblyFullPath, fileChangeService, this.HostDiagnosticUpdateSource, this.Id, this.Workspace, analyzerLoader, this.Language);
            this.AddOrUpdateAnalyzer(analyzerAssemblyFullPath, analyzer);

            if (PushingChangesToWorkspace)
            {
                var analyzerReference = analyzer.GetReference();
                this.ProjectTracker.NotifyWorkspace(workspace => workspace.OnAnalyzerReferenceAdded(Id, analyzerReference));

                List<VisualStudioAnalyzer> existingReferencesWithLoadErrors = GetCurrentAnalyzers().Where(a => a.HasLoadErrors).ToList();

                foreach (var existingReference in existingReferencesWithLoadErrors)
                {
                    this.ProjectTracker.NotifyWorkspace(workspace => workspace.OnAnalyzerReferenceRemoved(Id, existingReference.GetReference()));
                    existingReference.Reset();
                    this.ProjectTracker.NotifyWorkspace(workspace => workspace.OnAnalyzerReferenceAdded(Id, existingReference.GetReference()));
                }

                GetAnalyzerDependencyCheckingService().ReanalyzeSolutionForConflicts();
            }

            if (File.Exists(analyzerAssemblyFullPath))
            {
                GetAnalyzerFileWatcherService().TrackFilePathAndReportErrorIfChanged(analyzerAssemblyFullPath, projectId: Id);
            }
            else
            {
                analyzer.UpdatedOnDisk += OnAnalyzerChanged;
            }
            */
        }

        public void RemoveAnalyzerReference(string analyzerAssemblyFullPath)
        {
            AssertIsForeground();


            // TODO: reimplement
            /*

            if (!TryGetAnalyzer(analyzerAssemblyFullPath, out var analyzer))
            {
                return;
            }

            if (Workspace == null)
            {
                // This can happen only in tests.
                RemoveAnalyzer(analyzerAssemblyFullPath);
                analyzer.Dispose();
                return;
            }

            GetAnalyzerFileWatcherService().RemoveAnalyzerAlreadyLoadedDiagnostics(Id, analyzerAssemblyFullPath);

            RemoveAnalyzer(analyzerAssemblyFullPath);

            if (PushingChangesToWorkspace)
            {
                var analyzerReference = analyzer.GetReference();
                this.ProjectTracker.NotifyWorkspace(workspace => workspace.OnAnalyzerReferenceRemoved(Id, analyzerReference));

                GetAnalyzerDependencyCheckingService().ReanalyzeSolutionForConflicts();
            }

            analyzer.Dispose();
            */
        }
    }
}
