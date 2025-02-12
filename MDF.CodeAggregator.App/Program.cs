using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Generic;

namespace MDF.CodeAggregator.App
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("Usage: CodeConsolidator <solution_path> <project_name> <class_name> <output_dir>");
                return;
            }

            string solutionPath = args[0];
            string projectName = args[1];
            string className = args[2];
            string outputDir = args[3];

            Console.WriteLine("Solution Path: " + solutionPath);
            Console.WriteLine("Project Name: " + projectName);
            Console.WriteLine("Class Name: " + className);
            Console.WriteLine("Output Dir: " + outputDir);

            var workspace = MSBuildWorkspace.Create();
            Console.WriteLine("Loading Solution...");
            var solution = workspace.OpenSolutionAsync(solutionPath).Result;
            Console.WriteLine("Solution Loaded");
            var project = solution.Projects.FirstOrDefault(p => p.Name == projectName);

            if (project == null)
            {
                Console.WriteLine("Project not found.");
                return;
            }

            Console.WriteLine("Project Loaded " + project.Name);

            var document = project.Documents.FirstOrDefault(d => d.Name == className + ".cs");

            if (document == null)
            {
                Console.WriteLine("Class not found.");
                return;
            }
            Console.WriteLine("Class Loaded " + className);

            var syntaxTree = document.GetSyntaxTreeAsync().Result;
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            var namespaceDeclaration = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

            var outputFilePath = Path.Combine(outputDir, className + ".cs");

            Console.WriteLine("Writing to " + outputFilePath);

            using (var writer = new StreamWriter(outputFilePath))
            {
                // Write the static part
                writer.WriteLine("/*******");
                writer.WriteLine("* Read input from Console");
                writer.WriteLine("* Use: Console.WriteLine to output your result to STDOUT.");
                writer.WriteLine("* Use: Console.Error.WriteLine to output debugging information to STDERR;");
                writer.WriteLine("*/");
                writer.WriteLine();
                writer.WriteLine("namespace CSharpContestProject");
                writer.WriteLine("{");
                writer.WriteLine("    using System;");
                writer.WriteLine("    using System.Collections.Generic;");
                writer.WriteLine("    using App;");
                writer.WriteLine();
                writer.WriteLine("    class Program");
                writer.WriteLine("    {");
                writer.WriteLine("        static void Main(string[] args)");
                writer.WriteLine("        {");
                writer.WriteLine("            var result = Problem1.Solve();");
                writer.WriteLine("            Console.WriteLine(result);");
                writer.WriteLine("        }");
                writer.WriteLine("    }");
                writer.WriteLine("}");
                writer.WriteLine();

                // Write the class with its namespace and nested using directives
                WriteNamespace(writer, namespaceDeclaration, project, solution);
            }

            Console.WriteLine($"Generated {outputFilePath}");
        }

        static void WriteNamespace(StreamWriter writer, NamespaceDeclarationSyntax namespaceDeclaration, Project project, Solution solution)
        {
            var classDeclaration = namespaceDeclaration.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();

            if (classDeclaration != null)
            {
                writer.WriteLine($"namespace {namespaceDeclaration.Name}");
                writer.WriteLine("{");

                // Get using directives for this namespace
                var compilation = project.GetCompilationAsync().Result;
                var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

                var invokedSymbols = new HashSet<ISymbol>();
                var collector = new MethodInvocationCollector(semanticModel, invokedSymbols);
                collector.Visit(classDeclaration);

                var dependencies = new HashSet<string>();

                foreach (var symbol in invokedSymbols)
                {
                    var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (declaration != null)
                    {
                        var sourceDocument = solution.GetDocument(declaration.SyntaxTree);
                        var sourceRoot = sourceDocument.GetSyntaxRootAsync().Result;
                        var containingNamespace = sourceRoot.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>().FirstOrDefault(ns => ns.Members.Contains(declaration.Parent));

                        if (containingNamespace != null)
                        {
                            dependencies.Add(containingNamespace.Name.ToString());
                            writer.WriteLine($"    using {containingNamespace.Name};");
                        }
                    }
                }

                // Write the class definition
              
                writer.WriteLine(classDeclaration.ToFullString());

                writer.WriteLine("}");
            }

            // Include dependencies
            IncludeDependencies(writer, classDeclaration, project, solution);
        }

        static void IncludeDependencies(StreamWriter writer, ClassDeclarationSyntax classDeclaration, Project project, Solution solution)
        {
            var compilation = project.GetCompilationAsync().Result;
            var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

            var invokedSymbols = new HashSet<ISymbol>();
            var collector = new MethodInvocationCollector(semanticModel, invokedSymbols);
            collector.Visit(classDeclaration);

            foreach (var symbol in invokedSymbols)
            {
                var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (declaration != null)
                {
                    var sourceDocument = solution.GetDocument(declaration.SyntaxTree);
                    var sourceRoot = sourceDocument.GetSyntaxRootAsync().Result;
                    var containingNamespace = sourceRoot.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>().FirstOrDefault(ns => ns.Members.Contains(declaration.Parent));

                    if (containingNamespace != null)
                    {
                        // Write the namespace and its members
                        writer.WriteLine($"namespace {containingNamespace.Name}");
                        writer.WriteLine("{");

                        // Write using directives
                        var usingDirectives = containingNamespace.Usings;
                        foreach (var usingDirective in usingDirectives)
                        {
                            writer.WriteLine($"    using {usingDirective.Name};");
                        }

                        // Write class or method implementation
                        var classNode = declaration.Parent as ClassDeclarationSyntax;
                        if (classNode != null)
                        {
                            writer.WriteLine(classNode.ToFullString());
                        }
                        else
                        {
                            // If it's not a class (e.g., a method inside a class), write the outer class
                            var outerClass = declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                            if (outerClass != null)
                            {
                                writer.WriteLine(outerClass.ToFullString());
                            }
                        }

                        writer.WriteLine("}");
                    }
                }
            }
        }
        class MethodInvocationCollector : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly HashSet<ISymbol> _invokedSymbols;

            public MethodInvocationCollector(SemanticModel semanticModel, HashSet<ISymbol> invokedSymbols)
            {
                _semanticModel = semanticModel;
                _invokedSymbols = invokedSymbols;
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                {
                    _invokedSymbols.Add(symbol);
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                {
                    _invokedSymbols.Add(symbol);
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                {
                    _invokedSymbols.Add(symbol);
                }
                base.VisitMemberAccessExpression(node);
            }
        }
    }
}