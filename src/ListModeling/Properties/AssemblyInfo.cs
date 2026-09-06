using Rhino.PlugIns;
using System.Reflection;
using System.Runtime.InteropServices;

// Shown in Rhino under Options > Plug-ins > List Modeling > Details.
[assembly: PlugInDescription(DescriptionType.Organization, "John Haugabook")]
[assembly: PlugInDescription(DescriptionType.Email, "johnhaugabook@gmail.com")]

// Manifest resource name of the embedded plugin icon:
// <RootNamespace>.<FolderPath>.<file> per the EmbeddedResource item in the .csproj.
[assembly: PlugInDescription(DescriptionType.Icon, "ListModeling.Resources.ListModeling.ico")]

[assembly: AssemblyTitle("ListModeling")]
[assembly: AssemblyDescription("Model Rhino geometry from a Markdown list of parts: layers, orientation, dimensions, position and boolean actions.")]
[assembly: AssemblyCompany("John Haugabook")]
[assembly: AssemblyProduct("List Modeling")]
[assembly: AssemblyCopyright("Copyright 2026 John Haugabook")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// ===========================================================================
// THIS GUID IS THE PLUG-IN ID. Minted once for this plugin; never change it
// after release and never copy it into another project. Sibling plugins look
// this plugin up by this exact value (see docs/compatibility.md).
// ===========================================================================
[assembly: Guid("bc495df2-2c09-4174-b7a2-08371188cae7")]

// yak spec reads AssemblyInformationalVersion first, then AssemblyVersion.
[assembly: AssemblyVersion("0.0.0.0")]
[assembly: AssemblyFileVersion("0.0.0.0")]
[assembly: AssemblyInformationalVersion("0.0.0-alpha.1")]
