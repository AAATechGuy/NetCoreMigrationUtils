using System.Linq;
using System.Reflection;

namespace MigrationUtils
{
    public class FindAssemblyVersionProgram
    {
        public void FindAssemblyVersion(string assemblyPath)
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            Log.Info(assembly.ToIdString());

            foreach (var referencedAssembly in assembly.GetReferencedAssemblies().OrderBy(d => d.Name))
            {
                Log.Info($"-> {referencedAssembly.ToIdString()}");
            }
        }
    }
}