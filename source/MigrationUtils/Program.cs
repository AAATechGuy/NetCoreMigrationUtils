using System;
using System.Configuration;
using System.Linq;

namespace MigrationUtils
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintHelp();
                    return;
                }

                var command = args.GetInput<string>(0);
                args = args.Skip(1).ToArray();

                bool.TryParse(ConfigurationManager.AppSettings["DisableAutoResolveAssembly"], out bool disableAutoResolveAssembly);
                if (!disableAutoResolveAssembly)
                {
                    AssemblyResolverUtility.SetDefaultAssemblyResolver();
                }

                switch (command.ToLower())
                {
                    case "-?":
                    case "help":
                    case "--help":
                        PrintHelp();
                        return;
                    case "ver":
                        new FindAssemblyVersionProgram().FindAssemblyVersion(
                            assemblyPath: args.GetInput<string>(0));
                        break;
                    case "conflicts":
                        new FindConflictsProgram().FindConflictingReferences(
                            folder: args.GetInput<string>(0),
                            ignoreSystemLibs: args.GetInput<bool>(1, mandatory: false, defaultValue: true),
                            rootFilter: null);
                        break;
                    case "refs":
                        new FindConflictsProgram().FindReferencingAssemblies(
                            folder: args.GetInput<string>(0),
                            referencedAssemblyName: args.GetInput<string>(1))
                            .ToList();
                        break;
                    case "listdep":
                        new FindConflictsProgram().FindAllReferencedAssemblies(
                            folder: args.GetInput<string>(0),
                            referencedAssemblyName: args.GetInput<string>(1),
                            ignoreSystemLibs: args.GetInput<bool>(2, mandatory: false, defaultValue: true));
                        break;
                    case "graph":
                        ComplexityAnalyzer.CreateGraph(
                            sourceAssembliesFolder: args.GetInput<string>(0),
                            skipAssemblyFile: args.GetInput<string>(1, mandatory: false, defaultValue: ""),
                            targetFilepath: args.GetInput<string>(2, mandatory: false, defaultValue: "output.dgml"),
                            rootCsv: args.GetInput<string>(3, mandatory: false, defaultValue: ""));
                        break;
                    case "stats":
                        ComplexityAnalyzer.AnalyzeComplexity(
                            sourceAssembliesFolder: args.GetInput<string>(0),
                            skipAssemblyFile: args.GetInput<string>(1, mandatory: false, defaultValue: ""),
                            targetFilepath: args.GetInput<string>(2, mandatory: false, defaultValue: "output.stats.txt"),
                            rootCsv: args.GetInput<string>(3, mandatory: false, defaultValue: ""));
                        break;
                    default:
                        throw new Exception("invalid action specified");
                }
            }
            catch (Exception ex)
            {
                Log.Info($"An exception occurred: {ex}{Environment.NewLine}Type --help for commands");
            }
        }

        private static void PrintHelp()
        {
            Log.Info(@"
List of commands:

ver         {assemblyPath}
            about: gets assembly name, version, framework of a dll and its immediate dependencies.
            param: assemblyPath: path to assembly. 

conflicts   {sourceAssembliesFolder} [{ignoreSystemLibraries}:true]
            about: lists all assembly reference conflicts within a build drop. 
            param: sourceAssembliesFolder: folder that contains assemblies. 
            param: ignoreSystemLibraries: ignores conflicts for system assemblies. default is true. 

refs        {sourceAssembliesFolder} {referencedAssemblyName}
            about: find all assemblies in the source folder, that references the specified assembly. 
            param: sourceAssembliesFolder: folder that contains referencing assemblies. 
            param: referencedAssemblyName: assembly that is being referenced.

listdep     {sourceAssembliesFolder} {assemblyName} [{ignoreSystemLibraries}:true]
            about: list all nested dependencies for a given assembly. 
            param: sourceAssembliesFolder: used to extract assembly information of dependencies. 
            param: assemblyName: assembly whose dependencies need to be listed.
            param: ignoreSystemLibraries: ignores conflicts for system assemblies. default is true. 

graph       {sourceAssembliesFolder} [{ignoreList}] [{outputFile}:output.dgml] [{rootAssembliesCsv}]
            about: creates a dependency graph which highlights .net standard compatible binaries shown with weighted dependencies. 
            param: sourceAssembliesFolder: used to extract assembly information. 
            param: ignoreList: items to ignore in rendering. Can be filepath that contains newline-separated assemblies, 
                   or comma-separated assemblies.
            param: outputFile: output filepath of dgml file.
            param: rootAssembliesCsv: specify root assemblies while creating graph. This is optional.

stats       {sourceAssembliesFolder} [{ignoreList}] [{outputFile}:output.stats.txt]
            about: analyzes complexity of .net standard migration effort. 
                   - cluster assemblies based on framework 
                   - exclusive reference complexity (provides estimate of immediate migration effort)
                   - inclusive reference complexity (provides estimate of which assembly to pick-up next for migration)
                   PS, good to have, feature not supported now: highlight assemblies whose .net standard alternative is present in nuget. 
            param: sourceAssembliesFolder: used to extract assembly information. 
            param: ignoreList: items to ignore in rendering. Can be filepath that contains newline-separated assemblies, 
                   or comma-separated assemblies.
            param: outputFile: output filepath of analysis stats file.
            param: rootAssembliesCsv: specify root assemblies while creating graph. This is optional.
");
        }
    }

    internal static class Ext
    {
        internal static T GetInput<T>(this string[] args, int index, bool mandatory = true, T defaultValue = default(T))
        {
            if (args.Length <= index)
            {
                return !mandatory
                    ? defaultValue
                    : throw new ArgumentException("invalid param");
            }

            return (T)Convert.ChangeType(args[index], typeof(T));
        }
    }
}