using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace MDF.CodeAggregator.App
{
    internal class Program
    {
        private static readonly HashSet<string> ProcessedFullyQualifiedNames = new HashSet<string>();

        public static async Task Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: CodeConsolidator <solution_path> <fully_qualified_class_name> <output_dir>");
                return;
            }

            string solutionPath = args[0];
            string fullyQualifiedName = args[1]; // Full namespace + class name
            string outputDir = args[2];

            // Extract the project name and class name from fully qualified name
            var lastDotIndex = fullyQualifiedName.LastIndexOf('.');
            if (lastDotIndex == -1)
            {
                Console.WriteLine("Invalid fully qualified class name.");
                return;
            }

            string className = fullyQualifiedName.Substring(lastDotIndex + 1);
            string namespaceName = fullyQualifiedName.Substring(0, lastDotIndex);

            Console.WriteLine($"Solution Path: {solutionPath}");
            Console.WriteLine($"Fully Qualified Class Name: {fullyQualifiedName}");
            Console.WriteLine($"Namespace: {namespaceName}");
            Console.WriteLine($"Class Name: {className}");
            Console.WriteLine($"Output Dir: {outputDir}");

            var workspace = MSBuildWorkspace.Create();
            try
            {
                Console.WriteLine("Loading Solution...");
                var solution = await workspace.OpenSolutionAsync(solutionPath);
                Console.WriteLine("Solution Loaded");

                Document document = null;
                Project project = null; // Declare project outside the foreach loop

                foreach (var proj in solution.Projects)
                {
                    document = proj.Documents.FirstOrDefault(d =>
                    {
                        var syntaxTree = d.GetSyntaxTreeAsync().Result;
                        var root = syntaxTree.GetRoot();
                        var namespaceDeclaration = root.DescendantNodes()
                            .OfType<NamespaceDeclarationSyntax>()
                            .FirstOrDefault(nd => nd.Name.ToString() == namespaceName);

                        return namespaceDeclaration != null && namespaceDeclaration.Members
                            .OfType<ClassDeclarationSyntax>()
                            .Any(cd => cd.Identifier.Text == className);
                    });

                    if (document != null)
                    {
                        project = proj;
                        Console.WriteLine($"Project Loaded: {proj.Name}");
                        break;
                    }
                }

                if (document == null)
                {
                    Console.WriteLine("Class not found.");
                    return;
                }

                Console.WriteLine($"Class Loaded: {className}");

                var syntaxTree = await document.GetSyntaxTreeAsync();
                var root = (CompilationUnitSyntax)await syntaxTree.GetRootAsync();
                var namespaceDeclaration = root.Members
                    .OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault(nd => nd.Name.ToString() == namespaceName);

                var actualClassName = namespaceDeclaration?.Members
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(cd => cd.Identifier.Text == className)?
                    .Identifier.Text;

                if (actualClassName == null || namespaceDeclaration == null)
                {
                    Console.WriteLine("Class or namespace declaration not found.");
                    return;
                }

                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                var outputFilePath = Path.Combine(outputDir, actualClassName + ".cs");

                Console.WriteLine($"Writing to {outputFilePath}");

                using (var writer = new StreamWriter(outputFilePath))
                {
                    // Write the static part
                    writer.WriteLine("/*******");
                    writer.WriteLine("* Read input from Console");
                    writer.WriteLine("* Use: Console.WriteLine to output your result to STDOUT.");
                    writer.WriteLine("* Use: Console.Error.WriteLine to output debugging information to STDERR;");
                    writer.WriteLine("*/");
                    writer.WriteLine();
                    writer.WriteLine("using System;");
                    writer.WriteLine("using System.Linq;");
                    writer.WriteLine("using System.Drawing;");
                    writer.WriteLine("using System.Collections.Generic;");
                    writer.WriteLine();
                    writer.WriteLine("namespace CSharpContestProject");
                    writer.WriteLine("{");
                    writer.WriteLine($"    using {namespaceDeclaration.Name.ToString()};");
                    writer.WriteLine();
                    writer.WriteLine("    class Program");
                    writer.WriteLine("    {");
                    writer.WriteLine("        static void Main(string[] args)");
                    writer.WriteLine("        {");
                    writer.WriteLine($"            var result = {actualClassName}.Solve();");
                    writer.WriteLine("            Console.WriteLine(result);");
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    writer.WriteLine("}");
                    writer.WriteLine();

                    // Write the class with its namespace and nested using directives
                    await WriteNamespace(writer, namespaceDeclaration, project, solution);
                }

                Console.WriteLine($"Generated {outputFilePath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static async Task WriteNamespace(StreamWriter writer, NamespaceDeclarationSyntax namespaceDeclaration, Project project, Solution solution)
        {
            var classDeclaration = namespaceDeclaration.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration != null)
            {
                var namespaceName = namespaceDeclaration.Name.ToString();
                var fullyQualifiedName = $"{namespaceName}.{classDeclaration.Identifier.Text}";

                // Skip if the fully qualified name has already been processed
                if (!ProcessedFullyQualifiedNames.Add(fullyQualifiedName))
                {
                    return;
                }

                writer.WriteLine($"namespace {namespaceName}");
                writer.WriteLine("{");

                var compilation = await project.GetCompilationAsync();
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
                        var sourceRoot = await sourceDocument.GetSyntaxRootAsync();
                        var containingNamespace = sourceRoot.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>().FirstOrDefault(ns => ns.Members.Contains(declaration.Parent));

                        if (containingNamespace != null && dependencies.Add(containingNamespace.Name.ToString()))
                        {
                            writer.WriteLine($"    using {containingNamespace.Name};");
                        }
                    }
                }

                // Write the class definition and mark it as processed
                writer.WriteLine(classDeclaration.ToFullString());

                writer.WriteLine("}");

                // Include dependencies
                await IncludeDependencies(writer, classDeclaration, project, solution);
            }
        }

        static async Task IncludeDependencies(StreamWriter writer, ClassDeclarationSyntax classDeclaration, Project project, Solution solution)
        {
            var compilation = await project.GetCompilationAsync();
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
                    var sourceRoot = await sourceDocument.GetSyntaxRootAsync();
                    var containingNamespace = sourceRoot.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>().FirstOrDefault(ns => ns.Members.Contains(declaration.Parent));

                    if (containingNamespace != null)
                    {
                        // Write the namespace and its members if not already included
                        await WriteNamespaceIfNeeded(writer, containingNamespace, sourceDocument, solution);
                    }
                }
            }
        }

        static async Task WriteNamespaceIfNeeded(StreamWriter writer, NamespaceDeclarationSyntax namespaceDeclaration, Document sourceDocument, Solution solution)
        {
            var namespaceName = namespaceDeclaration.Name.ToString();

            var members = namespaceDeclaration.Members;
            foreach (var member in members)
            {
                if (member is ClassDeclarationSyntax classDecl)
                {
                    var fullyQualifiedName = $"{namespaceName}.{classDecl.Identifier.Text}";

                    // Check if the class has already been processed
                    if (ProcessedFullyQualifiedNames.Add(fullyQualifiedName))
                    {
                        writer.WriteLine($"namespace {namespaceName}");
                        writer.WriteLine("{");

                        // Write using directives
                        var usingDirectives = namespaceDeclaration.Usings;
                        foreach (var usingDirective in usingDirectives)
                        {
                            writer.WriteLine($"    using {usingDirective.Name};");
                        }

                        writer.WriteLine(classDecl.ToFullString());
                        writer.WriteLine("}");

                        await IncludeDependencies(writer, classDecl, sourceDocument.Project, solution);
                    }
                }
                else if (member is EnumDeclarationSyntax enumDecl)
                {
                    var fullyQualifiedName = $"{namespaceName}.{enumDecl.Identifier.Text}";

                    // Check if the enum has already been processed
                    if (ProcessedFullyQualifiedNames.Add(fullyQualifiedName))
                    {
                        writer.WriteLine($"namespace {namespaceName}");
                        writer.WriteLine("{");

                        // Write using directives
                        var usingDirectives = namespaceDeclaration.Usings;
                        foreach (var usingDirective in usingDirectives)
                        {
                            writer.WriteLine($"    using {usingDirective.Name};");
                        }

                        writer.WriteLine(enumDecl.ToFullString());
                        writer.WriteLine("}");
                    }
                }
                // Add more cases here if you need to handle other types of members like structs, interfaces, etc.
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
                Console.WriteLine($"\t -> Visiting invocation expression: {node}");

                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                {
                    _invokedSymbols.Add(symbol);
                }
                base.VisitInvocationExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                Console.WriteLine($"\t -> Visiting object creation expression: {node}");
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol == null)
                {
                    Console.Error.WriteLine($"\t\t -> Unable to resolve symbol for object creation at {node.GetLocation().GetLineSpan().StartLinePosition}");
                    Console.Error.WriteLine($"\t\t -> Candidate symbols: {string.Join(", ", symbolInfo.CandidateSymbols.Select(s => s.ToString()))}");
                }
                else
                {
                    Console.WriteLine($"\t\t -> Resolved symbol for object creation: {symbol}");
                    _invokedSymbols.Add(symbol);
                }
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                Console.WriteLine($"\t -> Visiting member access expression: {node}");

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

