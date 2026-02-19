using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using vs2026_plugin.Services;

namespace vs2026_plugin.Editor
{
    [ExportDiagnosticAnalyzer(LanguageNames.CSharp, Name = nameof(XygeniIssueDiagnosticAnalyzer))]
    [ExportDiagnosticAnalyzer(LanguageNames.VisualBasic, Name = nameof(XygeniIssueDiagnosticAnalyzer))]
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public sealed class XygeniIssueDiagnosticAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            XygeniErrorListService.SupportedDiagnosticDescriptors;

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
        }

        private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
        {
            if (string.IsNullOrWhiteSpace(context.Tree.FilePath))
            {
                return;
            }

            ImmutableArray<Diagnostic> diagnostics = XygeniErrorListService.GetDiagnosticsForFile(context.Tree.FilePath);
            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
