﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnusedLocalFunction
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.RemoveUnusedLocalFunction), Shared]
    [ExtensionOrder(After = PredefinedCodeFixProviderNames.AddImport)]
    internal class CSharpRemoveUnusedLocalFunctionCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        private const string CS8321 = nameof(CS8321); // The local function 'X' is declared but never used

        public sealed override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(CS8321);

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                new MyCodeAction(c => FixAsync(context.Document, context.Diagnostics.First(), c)),
                context.Diagnostics);

            return SpecializedTasks.EmptyTask;
        }

        protected override Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            var root = editor.OriginalRoot;
            var localFunctions = diagnostics.Select(d => root.FindToken(d.Location.SourceSpan.Start))
                                            .Select(t => t.GetAncestor<LocalFunctionStatementSyntax>())
                                            .WhereNotNull();

            foreach (var localFunction in localFunctions)
            {
                editor.RemoveNode(localFunction);
            }

            return SpecializedTasks.EmptyTask;
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(Func<CancellationToken, Task<Document>> createChangedDocument) :
                base(CSharpFeaturesResources.Remove_unused_function, createChangedDocument, CSharpFeaturesResources.Remove_unused_function)
            {
            }
        }
    }
}