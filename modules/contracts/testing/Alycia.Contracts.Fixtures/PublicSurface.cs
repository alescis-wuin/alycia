using System.Globalization;
using System.Reflection;

namespace Alycia.Contracts.Fixtures;

public static class PublicSurface
{
    public static string[] Read(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | BindingFlags.Instance | BindingFlags.Static;
        var lines = new List<string> { assembly.GetName().FullName };
        foreach (var type in assembly.GetExportedTypes())
        {
            var owner = type.FullName!;
            lines.Add($"{owner} {type.Attributes} : {type.BaseType} {string.Join(" ", type.GetInterfaces().Select(i => i.ToString()).Order(StringComparer.Ordinal))}");
            foreach (var parameter in type.GetGenericArguments().Where(p => p.IsGenericParameter)) lines.Add(owner + " " + Constraint(parameter));
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags))
                .Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
            {
                string parameters = string.Join(", ", method.GetParameters().Select(p =>
                    $"{p.ParameterType} {p.Name} {p.Attributes}" + (p.HasDefaultValue ? "=" + Convert.ToString(p.RawDefaultValue, CultureInfo.InvariantCulture) : "")));
                string generics = method is MethodInfo info && info.IsGenericMethod ? string.Join(", ", info.GetGenericArguments().Select(Constraint)) : "";
                lines.Add($"{owner} {method.Attributes} {(method as MethodInfo)?.ReturnType} {method.Name}({parameters}) {generics}".TrimEnd());
            }
            foreach (var field in type.GetFields(flags).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                lines.Add($"{owner} {field.Attributes} {field.FieldType} {field.Name}" +
                    (field.IsLiteral ? "=" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : ""));
        }
        return lines.Select(line => line.TrimEnd()).Order(StringComparer.Ordinal).ToArray();
    }

    private static string Constraint(Type parameter) =>
        $"{parameter.Name} {parameter.GenericParameterAttributes} {string.Join(" ", parameter.GetGenericParameterConstraints().Select(t => t.ToString()).Order(StringComparer.Ordinal))}".TrimEnd();
}
