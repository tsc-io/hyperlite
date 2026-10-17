using System;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: ComVisible(false)]
[assembly: CLSCompliant(true)]
[assembly: Guid("2D587F25-C918-4C14-B94F-0C2CD2E19E17")]
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// The signing key is not named here any more. It used to be ht4n.snk, through an
// AssemblyKeyFile attribute - obsolete, and pointing at a key from a repository this one
// does not contain. It is now src\Hyperlite.snk, one key for the whole product, named in
// Directory.Build.props where the compiler wants it.
