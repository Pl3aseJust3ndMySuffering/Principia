﻿namespace principia {
namespace sourcerer {

// Usage:
//   sourcerer --project:quantities \
//             --client:base --client:physics \
//             --exclude:macros.hpp --dry_run:false
// This will renamespace quantities and fix the references in the client
// projects.  The files will be overwritten.
internal class Sourcerer {
  static void Main(string[] args) {
    Renamespacer.Run(args);
  }
}

} // namespace sourcerer
} // namespace principia
