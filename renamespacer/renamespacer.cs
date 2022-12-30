﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace principia {
namespace renamespacer {
class Renamespacer {
  static void Main(string[] args) {
    // Parse the arguments.
    DirectoryInfo directory = null;
    bool move_files = false;
    foreach (string arg in args) {
      if (arg.StartsWith("--") && arg.Contains(":")) {
        string[] split = arg.Split(new []{"--", ":"}, StringSplitOptions.None);
        string option = split[1];
        string value = split[2];
        if (option == "directory") {
          directory = new DirectoryInfo(value);
        }
      } else if (arg == "--move") {
        move_files = true;
      }
    }

    // Find the files to process.
    FileInfo[] hpp_files = directory.GetFiles("*.hpp");
    FileInfo[] cpp_files = directory.GetFiles("*.cpp");
    FileInfo[] all_files = hpp_files.Union(cpp_files).ToArray();

    // Process the files, producing .new files.
    string project_namespace = directory.Name;
    foreach (FileInfo input_file in all_files) {
      string input_filename = input_file.FullName;
      string output_filename = input_file.DirectoryName + "\\" +
                               input_file.Name + ".new";
      string basename = Regex.Replace(input_file.Name,  "\\.[hc]pp$", "");
      string file_namespace = Regex.Replace(basename, "_body|_test", "");
      StreamWriter writer = File.CreateText(output_filename);
      var included_files = new List<string>();
      bool has_closed_file_namespace = false;
      bool has_closed_internal_namespace = false;
      bool has_emitted_new_style_usings = false;
      bool has_opened_file_namespace = false;
      bool has_seen_namespaces = false;
      bool has_seen_usings = false;

      StreamReader reader = input_file.OpenText();
      while (!reader.EndOfStream) {
        string line = reader.ReadLine();
        if (line.StartsWith("#include \"" + directory) &&
            (line.EndsWith(".hpp\"") || line.EndsWith(".cpp\""))) {
          // Collect the includes for files in |directory|, we'll need them to
          // generate the using-directives.
          included_files.Add(
              Regex.Replace(
                  Regex.Replace(line, "#include \"", ""),
                  "\\.[hc]pp\"$", ""));
          writer.WriteLine(line);
        } else if (line.StartsWith("namespace internal_")) {
          // The internal namespace gets wrapped into the file namespace and is
          // named "internal".
          writer.WriteLine("namespace " + file_namespace + " {");
          writer.WriteLine("namespace internal {");
          has_opened_file_namespace = true;
        } else if (line.StartsWith("namespace ")) {
          // Record that we have seen the first opening namespace.  Note that
          // this code assumes that all the opening namespaces are in sequence.
          writer.WriteLine(line);
          has_seen_namespaces = true;
        } else if (has_closed_internal_namespace &&
                   line.StartsWith("using internal_")) {
          // A using after the internal namespace has been closed, that exposes
          // the declaration to the outside.
          writer.WriteLine(Regex.Replace(line, "_" + file_namespace, ""));
        } else if (line.StartsWith("using ")) {
          // Record that we have seen the first using, but only emit it if it's
          // a new-style using-directive or a using-declaration for a different
          // directory.  Skip using-declations for internal stuff.
          if (line.StartsWith("using namespace") ||
              (!line.StartsWith("using " + project_namespace) &&
               !line.StartsWith("using internal_"))) {
            writer.WriteLine(line);
          }
          has_seen_usings = true;
        } else if (line.StartsWith("}  // namespace internal_")) {
          // Change the style of the line that closes the internal namespace.
          writer.WriteLine("}  // namespace internal");
          has_closed_internal_namespace = true;
        } else if (line.StartsWith("}  // namespace") &&
                   !has_closed_file_namespace) {
          // The close of a namespace, and we have not closed the file namespace
          // yet.  Do so now.
          writer.WriteLine("}  // namespace " + file_namespace);
          writer.WriteLine(line);
          has_closed_file_namespace = true;
        } else if (has_seen_usings && !has_emitted_new_style_usings) {
          // The first line that follows the using-declarations.  Emit the new
          // style using-directives here.
          foreach (string included_file in included_files) {
            writer.WriteLine("using namespace principia::" +
                              included_file.Replace("/", "::") + ";");
          }
          writer.WriteLine(line);
          has_emitted_new_style_usings = true;
        } else if (has_seen_namespaces && !has_opened_file_namespace) {
          // The first line that follows the opening namespaces.  Open the file
          // namespace if we haven't done so yet.
          writer.WriteLine("namespace " + file_namespace + " {");
          writer.WriteLine(line);
          has_opened_file_namespace = true;
        } else {
          writer.WriteLine(line);
        }
      }
      writer.Close();
      reader.Close();
      if (move_files) {
        File.Move(output_filename, input_filename);
      }
    }
  }
}

}  // namespace renamespacer
}  // namespace principia
