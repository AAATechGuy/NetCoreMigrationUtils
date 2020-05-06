using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

namespace MigrationUtils
{
    public static class Extensions
    {
        internal static T GetInput<T>(this string[] args, int index, bool mandatory = true, T defaultValue = default)
        {
            if (args.Length <= index)
            {
                return !mandatory
                    ? defaultValue
                    : throw new ArgumentException("invalid param");
            }

            return (T)Convert.ChangeType(args[index], typeof(T));
        }

        internal static Assembly ToAssembly(this AssemblyName assemblyName)
        {
            try { return Assembly.Load(assemblyName); } catch { return null; }
        }

        internal static string ToIdString(this AssemblyName assemblyName)
        {
            var assembly = assemblyName.ToAssembly();
            return $"{assemblyName.Name}, Version={assemblyName.Version}, Framework={assembly?.GetTargetFramework()}";
        }

        internal static string ToIdString(this Assembly assembly)
        {
            return $"{assembly.GetName().Name}, Version={assembly.GetName().Version}, Framework={assembly.GetTargetFramework()}";
        }

        internal static string GetTargetFramework(this Assembly assembly)
        {
            var targetFrameworkAttribute = assembly
                .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
                .SingleOrDefault()
                as TargetFrameworkAttribute;

            return targetFrameworkAttribute?.FrameworkName ?? "";
        }
    }
}