using System;
using System.IO;

namespace ListModeling.Syntax.Tests
{
  /// <summary>
  /// Locates repository folders from the test assembly's output directory, which sits
  /// several levels below the repository root and moves when the target framework or
  /// configuration changes. Walking up to a known marker keeps the tests runnable from
  /// any working directory.
  /// </summary>
  public static class RepoPaths
  {
    static readonly Lazy<string> RootPath = new Lazy<string>(FindRoot);

    public static string Root => RootPath.Value;
    public static string Examples => Path.Combine(Root, "examples");
    public static string Fixtures => Path.Combine(Root, "tests", "fixtures");
    public static string Golden => Path.Combine(Root, "tests", "golden");

    static string FindRoot()
    {
      var directory = new DirectoryInfo(AppContext.BaseDirectory);
      while (directory != null)
      {
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "ListModeling")))
          return directory.FullName;
        directory = directory.Parent;
      }
      throw new DirectoryNotFoundException(
        "Could not locate the repository root above " + AppContext.BaseDirectory);
    }
  }
}
