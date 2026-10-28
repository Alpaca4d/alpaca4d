#!/usr/bin/env bash
# Builds FemDesign.Communication.dll for .NET Framework 4.8 - which Rhino 7 and 8 load - out of
# femdesign-api-2.0, which targets net9.0. femdesign-api-2.0 is not changed: its StruXml sources
# are copied to a temporary folder and built there with a project file of our own.
#
# Only the StruXml part is built, which is all a .struxml export needs. The named-pipe connection,
# the result tables and FdScript are left out. What net48 lacks is filled in by the polyfills
# below, and LineLoad.cs loses a using it never needed, which net48 cannot resolve.
#
#   ./build-net48.sh                     femdesign-api-2.0 next to this repository, under ms-structural
#   SRC=/path/to/communication ./build-net48.sh
set -euo pipefail
cd "$(dirname "$0")"
here=$(pwd)

SRC=${SRC:-../../../ms-structural/femdesign-api-2.0/src/integration/femdesign/communication}
[ -d "$SRC/strusoft.interop/struxml" ] || { echo "no femdesign-api-2.0 communication project at $SRC; set SRC=..."; exit 1; }
SRC=$(cd "$SRC" && pwd)

commit=$(git -C "$SRC" rev-parse --short HEAD 2>/dev/null || echo unknown)
git -C "$SRC" diff --quiet HEAD -- . 2>/dev/null || commit="$commit+local changes"

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
mkdir -p "$work/strusoft.interop"
cp -R "$SRC/strusoft.interop/struxml" "$SRC/strusoft.interop/utils" "$work/strusoft.interop/"
sed -i.bak '/^using System\.Reflection\.Metadata;/d' "$work/strusoft.interop/struxml/partials/loads/LineLoad.cs"
rm "$work/strusoft.interop/struxml/partials/loads/LineLoad.cs.bak"

cat > "$work/FemDesign.Communication.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FemDesign.Communication</RootNamespace>
    <AssemblyName>FemDesign.Communication</AssemblyName>
    <InformationalVersion>femdesign-api-2.0 $commit, StruXml only, net48</InformationalVersion>
    <NoWarn>\$(NoWarn);CS1591;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="strusoft.interop/struxml/FDschema.xsd" LogicalName="fdschema.xsd" />
    <EmbeddedResource Include="strusoft.interop/struxml/partials/sections/sectiondatabase.struxml" LogicalName="sectiondatabase.struxml" />
    <EmbeddedResource Include="strusoft.interop/struxml/partials/materials/*.struxml" LogicalName="%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
EOF

cat > "$work/Net48Polyfills.cs" <<'EOF'
// What the StruXml sources use from .NET 5 to 9 and .NET Framework 4.8 does not have.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) { ParameterName = parameterName; }
        public string ParameterName { get; }
    }
}

namespace System.Collections.Generic
{
    internal static class Net48CollectionPolyfills
    {
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
                return false;
            dictionary.Add(key, value);
            return true;
        }

        public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
            => dictionary.TryGetValue(key, out var value) ? value : default!;
    }
}

namespace System.Linq
{
    internal static class Net48LinqPolyfills
    {
        public static IEnumerable<Collections.Generic.KeyValuePair<TKey, int>> CountBy<TSource, TKey>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
            => source.GroupBy(keySelector).Select(g => new Collections.Generic.KeyValuePair<TKey, int>(g.Key, g.Count()));

        public static Collections.Generic.Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(
            this IEnumerable<Collections.Generic.KeyValuePair<TKey, TValue>> source)
            => source.ToDictionary(p => p.Key, p => p.Value);
    }
}
EOF

dotnet build "$work/FemDesign.Communication.csproj" -c Release -o "$work/out" -nologo -v:q
cp "$work/out/FemDesign.Communication.dll" "$here/"
echo "FemDesign.Communication.dll built from femdesign-api-2.0 $commit"
