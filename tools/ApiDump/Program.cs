// Dump public members of types in a .NET assembly (metadata only — does not load the assembly).
// usage: ApiDump <assembly.dll> [type-name-substring]
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: ApiDump <assembly.dll> [type-substring]");
    return 2;
}

string path = args[0];
string needle = args.Length > 1 ? args[1] : null;

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

int shown = 0;
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    string ns = md.GetString(td.Namespace);
    string nm = md.GetString(td.Name);
    string full = string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;

    if (needle != null && full.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

    shown++;
    var bt = td.BaseType;
    string baseName = "";
    if (!bt.IsNil)
    {
        try { baseName = " : " + md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)bt).Name); }
        catch { try { baseName = " : " + md.GetString(md.GetTypeReference((TypeReferenceHandle)bt).Name); } catch { } }
    }
    Console.WriteLine("=== " + full + baseName);

    foreach (var ih in td.GetInterfaceImplementations())
    {
        var ii = md.GetInterfaceImplementation(ih);
        try { Console.WriteLine("    impl " + md.GetString(md.GetTypeReference((TypeReferenceHandle)ii.Interface).Name)); }
        catch { try { Console.WriteLine("    impl " + md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)ii.Interface).Name)); } catch { } }
    }

    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        Console.WriteLine("    prop " + md.GetString(p.Name));
    }

    foreach (var fh in td.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        Console.WriteLine("    field " + md.GetString(f.Name));
    }

    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string n = md.GetString(m.Name);
        if (n.StartsWith("get_") || n.StartsWith("set_")) continue;   // already shown as props
        Console.WriteLine("    " + n);
    }
}

Console.WriteLine();
Console.WriteLine("types matched: " + shown);
return 0;
