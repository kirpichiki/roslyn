using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem.Legacy
{
    /// <summary>
    /// Creates batch scopes for projects based on IVsSolutionEvents. This is useful for projects types that don't otherwise have
    /// good batching concepts.
    /// </summary>
    /// <remarks>All members of this class are affinitized to the UI thread.</remarks>
    [Export(typeof(SolutionEventsBatchScopeCreator))]
    internal sealed class SolutionEventsBatchScopeCreator : ForegroundThreadAffinitizedObject
    {
        /// <summary>
        /// Map of active <see cref="VisualStudioProject.BatchScope"/> for projects being loaded in a main solution load.
        /// </summary>
        private readonly Dictionary<VisualStudioProject, VisualStudioProject.BatchScope> _fullSolutionLoadScopes = new Dictionary<VisualStudioProject, VisualStudioProject.BatchScope>();

        /// <summary>
        /// Map of active <see cref="VisualStudioProject.BatchScope"/> for projects being loaded in a foreground batch. May be null
        /// if there isn't a foreground batch.
        /// </summary>
        private Dictionary<VisualStudioProject, VisualStudioProject.BatchScope> _foregroundProjectLoadScopes;

        private readonly IServiceProvider _serviceProvider;

        private bool _isSubscribedToSolutionEvents = false;
        private bool _solutionLoaded = false;

        [ImportingConstructor]
        public SolutionEventsBatchScopeCreator(IThreadingContext threadingContext, [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider)
            : base(threadingContext, assertIsForeground: false)
        {
            _serviceProvider = serviceProvider;
        }

        public void StartTrackingProject(VisualStudioProject project)
        {
            AssertIsForeground();

            EnsureSubcribedToSolutionEvents();

            if (!_solutionLoaded)
            {
                _fullSolutionLoadScopes.Add(project, project.CreateBatchScope());
            }
        }

        public void StopTrackingProject(VisualStudioProject project)
        {
            AssertIsForeground();

            void RemoveScopeFromMap(Dictionary<VisualStudioProject, VisualStudioProject.BatchScope> scopeMap)
            {
                if (scopeMap != null && scopeMap.TryGetValue(project, out var scope))
                {
                    scope.Dispose();
                    scopeMap.Remove(project);
                }
            }

            RemoveScopeFromMap(_fullSolutionLoadScopes);
            RemoveScopeFromMap(_foregroundProjectLoadScopes);
        }

        private void CompleteScopes(Dictionary<VisualStudioProject, VisualStudioProject.BatchScope> scopes)
        {
            AssertIsForeground();

            foreach (var scope in scopes.Values)
            {
                scope.Dispose();
            }

            scopes.Clear();
        }

        private void EnsureSubcribedToSolutionEvents()
        {
            AssertIsForeground();

            if (_isSubscribedToSolutionEvents)
            {
                return;
            }

            var solution = (IVsSolution)_serviceProvider.GetService(typeof(SVsSolution));

            // We never unsubscribe from this, so we just throw out the cookie. We could consider unsubscribing if/when all our
            // projects are unloaded, but it seems fairly unecessary -- it'd only be useful if somebody closed one solution but then
            // opened other solutions in entirely different languages from there.
            solution.AdviseSolutionEvents(new EventSink(this), out _);

            // It's possible that we're loading after the solution has already fully loaded, so see if we missed the event 
            var shellMonitorSelection = (IVsMonitorSelection)_serviceProvider.GetService(typeof(SVsShellMonitorSelection));

            if (ErrorHandler.Succeeded(shellMonitorSelection.GetCmdUIContextCookie(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_guid, out var fullyLoadedContextCookie)))
            {
                if (ErrorHandler.Succeeded(shellMonitorSelection.IsCmdUIContextActive(fullyLoadedContextCookie, out var fActive)) && fActive != 0)
                {
                    _solutionLoaded = true;
                }
            }
        }

        private class EventSink : IVsSolutionEvents, IVsSolutionLoadEvents
        {
            private readonly SolutionEventsBatchScopeCreator _scopeCreator;

            public EventSink(SolutionEventsBatchScopeCreator scopeCreator)
            {
                _scopeCreator = scopeCreator;
            }

            int IVsSolutionEvents.OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionEvents.OnBeforeCloseSolution(object pUnkReserved)
            {
                _scopeCreator._solutionLoaded = false;

                return VSConstants.S_OK;
            }

            int IVsSolutionEvents.OnAfterCloseSolution(object pUnkReserved)
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionLoadEvents.OnBeforeOpenSolution(string pszSolutionFilename)
            {
                Contract.ThrowIfTrue(_scopeCreator._fullSolutionLoadScopes.Any());

                _scopeCreator._solutionLoaded = false;

                return VSConstants.S_OK;
            }

            int IVsSolutionLoadEvents.OnBeforeBackgroundSolutionLoadBegins()
            {
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionLoadEvents.OnQueryBackgroundLoadProjectBatch(out bool pfShouldDelayLoadToNextIdle)
            {
                pfShouldDelayLoadToNextIdle = false;
                return VSConstants.E_NOTIMPL;
            }

            int IVsSolutionLoadEvents.OnBeforeLoadProjectBatch(bool fIsBackgroundIdleBatch)
            {
                if (!fIsBackgroundIdleBatch)
                {
                    _scopeCreator._foregroundProjectLoadScopes = new Dictionary<VisualStudioProject, VisualStudioProject.BatchScope>();
                }

                return VSConstants.S_OK;
            }

            int IVsSolutionLoadEvents.OnAfterLoadProjectBatch(bool fIsBackgroundIdleBatch)
            {
                if (_scopeCreator._foregroundProjectLoadScopes != null)
                {
                    _scopeCreator.CompleteScopes(_scopeCreator._foregroundProjectLoadScopes);
                    _scopeCreator._foregroundProjectLoadScopes = null;
                }

                return VSConstants.S_OK;
            }

            int IVsSolutionLoadEvents.OnAfterBackgroundSolutionLoadComplete()
            {
                _scopeCreator.CompleteScopes(_scopeCreator._fullSolutionLoadScopes);

                return VSConstants.S_OK;
            }
        }
    }
}
