// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices.Implementation.CodeModel;
using Microsoft.VisualStudio.LanguageServices.Implementation.EditAndContinue;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem
{
    using Workspace = Microsoft.CodeAnalysis.Workspace;

    // NOTE: Microsoft.VisualStudio.LanguageServices.TypeScript.TypeScriptProject derives from AbstractProject.
#pragma warning disable CS0618 // IVisualStudioHostProject is obsolete
    internal abstract partial class AbstractProject : ForegroundThreadAffinitizedObject, IVisualStudioHostProject
#pragma warning restore CS0618 // IVisualStudioHostProject is obsolete
    {
        internal const string ProjectGuidPropertyName = "ProjectGuid";

        internal static object RuleSetErrorId = new object();

        private readonly DiagnosticDescriptor _errorReadingRulesetRule = new DiagnosticDescriptor(
            id: IDEDiagnosticIds.ErrorReadingRulesetId,
            title: ServicesVSResources.ErrorReadingRuleset,
            messageFormat: ServicesVSResources.Error_reading_ruleset_file_0_1,
            category: FeaturesResources.Roslyn_HostError,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);


        public AbstractProject(
            Func<ProjectId, IVsReportExternalErrors> reportExternalErrorCreatorOpt,
            string projectSystemName,
            string projectFilePath,
            IVsHierarchy hierarchy,
            string language,
            Guid projectGuid,
            IServiceProvider serviceProvider,
            IThreadingContext threadingContext,
            HostDiagnosticUpdateSource hostDiagnosticUpdateSourceOpt,
            ICommandLineParserService commandLineParserServiceOpt = null)
            : base(threadingContext)
        {
            ServiceProvider = serviceProvider;
            Hierarchy = hierarchy;
            Guid = projectGuid;

            var displayName = hierarchy != null && hierarchy.TryGetName(out var name) ? name : projectSystemName;
            this.DisplayName = displayName;

            ProjectSystemName = projectSystemName;
            HostDiagnosticUpdateSource = hostDiagnosticUpdateSourceOpt;

            // Set the default value for last design time build result to be true, until the project system lets us know that it failed.
            LastDesignTimeBuildSucceeded = true;

            UpdateProjectDisplayNameAndFilePath(displayName, projectFilePath);

            if (ProjectFilePath != null)
            {
                Version = VersionStamp.Create(File.GetLastWriteTimeUtc(ProjectFilePath));
            }
            else
            {
                Version = VersionStamp.Create();
            }

            if (reportExternalErrorCreatorOpt != null)
            {
                ExternalErrorReporter = reportExternalErrorCreatorOpt(Id);
            }


            UpdateAssemblyName();

            Logger.Log(FunctionId.AbstractProject_Created,
                KeyValueLogMessage.Create(LogType.Trace, m =>
                {
                    m[ProjectGuidPropertyName] = Guid;
                }));
        }

        internal IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Indicates whether this project is a website type.
        /// </summary>
        public bool IsWebSite { get; protected set; }

        /// <summary>
        /// A full path to the project obj output binary, or null if the project doesn't have an obj output binary.
        /// </summary>
        internal string ObjOutputPath { get; private set; }

        /// <summary>
        /// A full path to the project bin output binary, or null if the project doesn't have an bin output binary.
        /// </summary>
        internal string BinOutputPath { get; private set; }

        public IReferenceCountedDisposable<IRuleSetFile> RuleSetFile { get; private set; }

        protected VisualStudioProjectTracker ProjectTracker { get; }

        protected IVsReportExternalErrors ExternalErrorReporter { get; }

        internal HostDiagnosticUpdateSource HostDiagnosticUpdateSource { get; }

        public ProjectId Id { get; }

        public string Language { get; }

        private ICommandLineParserService CommandLineParserService { get; }

        /// <summary>
        /// The <see cref="IVsHierarchy"/> for this project.  NOTE: May be null in Deferred Project Load cases.
        /// </summary>
        public IVsHierarchy Hierarchy { get; }

        /// <summary>
        /// Guid of the project
        /// 
        /// it is not readonly since it can be changed while loading project
        /// </summary>
        public Guid Guid { get; protected set; }

        public Workspace Workspace { get; }

        public VersionStamp Version { get; }

        public IProjectCodeModel ProjectCodeModel { get; protected set; }
        
        /// <summary>
        /// The containing directory of the project. Null if none exists (consider Venus.)
        /// </summary>
        protected string ContainingDirectoryPathOpt
        {
            get
            {
                var projectFilePath = this.ProjectFilePath;
                if (projectFilePath != null)
                {
                    return Path.GetDirectoryName(projectFilePath);
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// The full path of the project file. Null if none exists (consider Venus.)
        /// Note that the project file path might change with project file rename.
        /// If you need the folder of the project, just use <see cref="ContainingDirectoryPathOpt" /> which doesn't change for a project.
        /// </summary>
        public string ProjectFilePath { get; private set; }

        /// <summary>
        /// The public display name of the project. This name is not unique and may be shared
        /// between multiple projects, especially in cases like Venus where the intellisense
        /// projects will match the name of their logical parent project.
        /// </summary>
        public string DisplayName { get; private set; }

        internal string AssemblyName { get; private set; }

        /// <summary>
        /// The name of the project according to the project system. In "regular" projects this is
        /// equivalent to <see cref="DisplayName"/>, but in Venus cases these will differ. The
        /// ProjectSystemName is the 2_Default.aspx project name, whereas the regular display name
        /// matches the display name of the project the user actually sees in the solution explorer.
        /// These can be assumed to be unique within the Visual Studio workspace.
        /// </summary>
        public string ProjectSystemName { get; }

        /// <summary>
        /// Flag indicating if the latest design time build has succeeded for current project state.
        /// </summary>
        /// <remarks>Default value is true.</remarks>
        protected bool LastDesignTimeBuildSucceeded { get; private set; }

        internal VsENCRebuildableProjectImpl EditAndContinueImplOpt { get; private set; }

        protected void SetIntellisenseBuildResultAndNotifyWorkspace(bool succeeded)
        {
            // set IntelliSense related info
            LastDesignTimeBuildSucceeded = succeeded;

            Logger.Log(FunctionId.AbstractProject_SetIntelliSenseBuild,
                KeyValueLogMessage.Create(LogType.Trace, m =>
                {
                    m[ProjectGuidPropertyName] = Guid;
                    m[nameof(LastDesignTimeBuildSucceeded)] = LastDesignTimeBuildSucceeded;
                }));

            ////if (PushingChangesToWorkspace)
            ////{
            ////    // set workspace reference info
            ////    ProjectTracker.NotifyWorkspace(workspace => workspace.OnHasAllInformationChanged(Id, succeeded));
            ////}
        }

        protected ImmutableArray<string> GetStrongNameKeyPaths()
        {
            var outputPath = this.ObjOutputPath;

            if (this.ContainingDirectoryPathOpt == null && outputPath == null)
            {
                return ImmutableArray<string>.Empty;
            }

            var builder = ArrayBuilder<string>.GetInstance();
            if (this.ContainingDirectoryPathOpt != null)
            {
                builder.Add(this.ContainingDirectoryPathOpt);
            }

            if (outputPath != null)
            {
                builder.Add(Path.GetDirectoryName(outputPath));
            }

            return builder.ToImmutableAndFree();
        }

        private static string GetAssemblyNameFromPath(string outputPath)
        {
            Debug.Assert(outputPath != null);

            // dev11 sometimes gives us output path w/o extension, so removing extension becomes problematic
            if (outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                outputPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                outputPath.EndsWith(".netmodule", StringComparison.OrdinalIgnoreCase) ||
                outputPath.EndsWith(".winmdobj", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(outputPath);
            }
            else
            {
                return Path.GetFileName(outputPath);
            }
        }

        protected bool CanConvertToProjectReferences
        {
            get
            {
                if (this.Workspace != null)
                {
                    return this.Workspace.Options.GetOption(InternalFeatureOnOffOptions.ProjectReferenceConversion);
                }
                else
                {
                    return InternalFeatureOnOffOptions.ProjectReferenceConversion.DefaultValue;
                }
            }
        }

        protected bool CanAddProjectReference(ProjectReference projectReference)
        {
            // TODO: figure out who should be calling this

            if (projectReference.ProjectId == this.Id)
            {
                // cannot self reference
                return false;
            }

            var otherProject = this.Workspace.CurrentSolution.GetProject(projectReference.ProjectId);
            if (otherProject != null)
            {
                // We won't allow project-to-project references if this one supports compilation and the other one doesn't.
                // This causes problems because if we then try to create a compilation, we'll fail even though it would have worked with
                // a metadata reference. If neither supports compilation, we'll let the reference go through on the assumption the
                // language (TypeScript/F#, etc.) is doing that intentionally.
                if (this.Language != otherProject.Language && 
                    this.ProjectTracker.WorkspaceServices.GetLanguageServices(this.Language).GetService<ICompilationFactoryService>() != null &&
                    this.ProjectTracker.WorkspaceServices.GetLanguageServices(otherProject.Language).GetService<ICompilationFactoryService>() == null)
                {
                    return false;
                }

                // cannot add a reference to a project that references us (it would make a cycle)
                var dependencyGraph = this.Workspace.CurrentSolution.GetProjectDependencyGraph();

                if (dependencyGraph.GetProjectsThatThisProjectTransitivelyDependsOn(otherProject.Id).Contains(this.Id))
                {
                    return false;
                }
            }

            return true;
        }

        protected void UpdateRuleSetError(IRuleSetFile ruleSetFile)
        {
            AssertIsForeground();

            if (this.HostDiagnosticUpdateSource == null)
            {
                return;
            }

            if (ruleSetFile == null ||
                ruleSetFile.GetException() == null)
            {
                this.HostDiagnosticUpdateSource.ClearDiagnosticsForProject(this.Id, RuleSetErrorId);
            }
            else
            {
                var messageArguments = new string[] { ruleSetFile.FilePath, ruleSetFile.GetException().Message };
                if (DiagnosticData.TryCreate(_errorReadingRulesetRule, messageArguments, this.Id, this.Workspace, out var diagnostic))
                {
                    this.HostDiagnosticUpdateSource.UpdateDiagnosticsForProject(this.Id, RuleSetErrorId, SpecializedCollections.SingletonEnumerable(diagnostic));
                }
            }
        }

        protected void SetObjOutputPathAndRelatedData(string objOutputPath)
        {
            AssertIsForeground();
        }

        private void UpdateAssemblyName()
        {
            AssertIsForeground();

            // set assembly name if changed
            // we use designTimeOutputPath to get assembly name since it is more reliable way to get the assembly name.
            // otherwise, friend assembly all get messed up.
            var newAssemblyName = GetAssemblyNameFromPath(this.ObjOutputPath ?? this.ProjectSystemName);
            if (!string.Equals(AssemblyName, newAssemblyName, StringComparison.Ordinal))
            {
                AssemblyName = newAssemblyName;

                /*
                if (PushingChangesToWorkspace)
                {
                    this.ProjectTracker.NotifyWorkspace(workspace => workspace.OnAssemblyNameChanged(this.Id, newAssemblyName));
                }
                */
            }
        }

        protected internal void SetBinOutputPathAndRelatedData(string binOutputPath)
        {
            AssertIsForeground();

            // refresh final output path
            var currentBinOutputPath = this.BinOutputPath;
            if (binOutputPath != null && !string.Equals(currentBinOutputPath, binOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                this.BinOutputPath = binOutputPath;
            }
        }

        protected void UpdateProjectDisplayName(string newDisplayName)
        {
            UpdateProjectDisplayNameAndFilePath(newDisplayName, newFilePath: null);
        }

        protected void UpdateProjectFilePath(string newFilePath)
        {
            UpdateProjectDisplayNameAndFilePath(newDisplayName: null, newFilePath: newFilePath);
        }

        protected void UpdateProjectDisplayNameAndFilePath(string newDisplayName, string newFilePath)
        {
            AssertIsForeground();

            if (newDisplayName != null && this.DisplayName != newDisplayName)
            {
                this.DisplayName = newDisplayName;
            }

            if (newFilePath != null && File.Exists(newFilePath) && this.ProjectFilePath != newFilePath)
            {
                Debug.Assert(PathUtilities.IsAbsolute(newFilePath));
                this.ProjectFilePath = newFilePath;
            }

        }

        private static MetadataReferenceResolver CreateMetadataReferenceResolver(IMetadataService metadataService, string projectDirectory, string outputDirectory)
        {
            ImmutableArray<string> assemblySearchPaths;
            if (projectDirectory != null && outputDirectory != null)
            {
                assemblySearchPaths = ImmutableArray.Create(projectDirectory, outputDirectory);
            }
            else if (projectDirectory != null)
            {
                assemblySearchPaths = ImmutableArray.Create(projectDirectory);
            }
            else if (outputDirectory != null)
            {
                assemblySearchPaths = ImmutableArray.Create(outputDirectory);
            }
            else
            {
                assemblySearchPaths = ImmutableArray<string>.Empty;
            }

            return new WorkspaceMetadataFileReferenceResolver(metadataService, new RelativePathResolver(assemblySearchPaths, baseDirectory: projectDirectory));
        }

        /// <summary>
        /// Used for unit testing: don't crash the process if something bad happens.
        /// </summary>
        internal static bool CrashOnException = true;

        protected static bool FilterException(Exception e)
        {
            if (CrashOnException)
            {
                FatalError.Report(e);
            }

            // Nothing fancy, so don't catch
            return false;
        }

        #region FolderNames
       
        #endregion
    }
}
