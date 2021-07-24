using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Mono.Options;
using Colorful;
using Console = Colorful.Console;

namespace gitty {
  class Program {

    static void Main(string[] args) {
      Console.WriteLine("Hello World!");
      if (args.Length == 0) {
        Console.WriteLine("Nothing to do.");
        return;
      }

      foreach (var arg in args) {
        Console.WriteLine($"arg: {arg}");
      }

      if (args[0] == "line-endings") { }

      if (args[0] == "by-author") {
        var restArgs = args.Skip(2).ToArray();
        var numberOfCommits = 1;
        var repoLocation = ".";
        var authorEmail = "";
        var compareWhat = "";
        var options = new OptionSet {
          {"n|number=", "Number of commits.", (int n) => numberOfCommits = n},
          {"r|repository=", "Repository location.", (r) => repoLocation = r},
          {"e|email=", "Author e-mail (default to local current user).", (e) => authorEmail = e},
          {"c|compare=", "What to compare against when showing diff.", c => compareWhat = c}
        };
        try {
          var extra = options.Parse(restArgs);
          Console.WriteLine($"{numberOfCommits} commits to scan");
          Console.WriteLine($"{repoLocation} repository location.");
          Console.WriteLine($"{(extra.Count != 0 ? extra.Aggregate((a, b) => a + "," + b) : "")}");
          using (var repo = new Repository(repoLocation)) {
            var authorConfig = repo.Config.Get<string>("user.email", ConfigurationLevel.Local);
            authorEmail = String.IsNullOrWhiteSpace(authorEmail) ? authorConfig.Value : authorEmail;
            var commits = new List<Commit>();
            foreach (var commit in repo.Commits.ToList().Where(c =>
              c.Author.Email.Equals(authorEmail, StringComparison.InvariantCultureIgnoreCase))) {
              Console.WriteLine($"{commit.Author.When.ToString()} {commit.MessageShort}");
              commits.Add(commit);
              var patch = repo.Diff.Compare<Patch>(commit.Tree, repo.Head.Tip.Tree);
              foreach (var pec in patch) {
                Console.WriteLine("{0} = {1} ({2}+ and {3}-)",
                  pec.Path,
                  pec.LinesAdded + pec.LinesDeleted,
                  pec.LinesAdded,
                  pec.LinesDeleted);
                // foreach (var line in pec.AddedLines) {
                //   Console.WriteLine($"+{line.LineNumber} {line.Content}");
                // }
                // PatchEntryChanges entryChanges = patch["path/to/my/file.txt"];

                // foreach (var line in pec.DeletedLines) {
                //   Console.WriteLine($"-{line.LineNumber} {line.Content}");
                // }

                var lines = pec.AddedLines.Select(l => l.LineNumber)
                  .Intersect(pec.DeletedLines.Select(l => l.LineNumber));
                var lineNumbers = pec.AddedLines.Select(l => l.LineNumber)
                  .Concat(pec.DeletedLines.Select(d => d.LineNumber))
                  .Distinct()
                  .OrderBy(d => d);
                
                var deletedSameStyle = new StyleSheet(Color.Red);
                var deletedDiffStyle = new StyleSheet(Color.Brown);
                var addedSameStyle = new StyleSheet(Color.Cyan);
                var addedDiffStyle = new StyleSheet(Color.Green);
                foreach (var lineNumber in lineNumbers) {
                  var added = pec.AddedLines.Where(l => l.LineNumber == lineNumber);
                  var deleted = pec.DeletedLines.Where(l => l.LineNumber == lineNumber);

                  if (added.Any() && deleted.Any()) {
                    GitAdapter.Diff2Lines(added.First(), deleted.First());
                  } else if (added.Any()) {
                    added.First().Content
                      .Select(c => GitAdapter.FormatChar(c))
                      .ToList().ForEach(c => Console.WriteStyled(c, addedDiffStyle));
                    Console.Write("\n");
                  } else if (deleted.Any()) {
                    deleted.First().Content
                      .Select(c => GitAdapter.FormatChar(c))
                      .ToList().ForEach(c => Console.WriteStyled(c, deletedDiffStyle));
                    Console.Write("\n");
                  }

                }
              }

              foreach (var treeEntry in commit.Tree) {
                Console.WriteLine($"{treeEntry.Mode} mode");
                Console.WriteLine($"{treeEntry.Name} name");
                Console.WriteLine($"{treeEntry.Path} path");
                Console.WriteLine($"{treeEntry.Target.Sha} target sha");
                Console.WriteLine($"{treeEntry.TargetType} target type");
              }
            }
          }
        }
        catch (OptionException ex) {
          Console.WriteLine("Arguments not understood.");
        }
      }
    }
  }

  static class GitAdapter {
    public static string FormatChar(char c) {
      if (c == '\r') {
        return "\\r";
      }

      if (c == '\n') {
        return "\\n";
      }

      if (c == '\t') {
        return "\\t";
      }

      return c.ToString();
    }
    public static void GetUnmergedFiles() {
      using (var repo = new Repository(".")) {
        foreach (IndexEntry e in repo.Index) {
          if (e.StageLevel == 0) {
            continue;
          }

          Console.WriteLine("{0} {1} {2}       {3}",
            Convert.ToString((int) e.Mode, 8),
            e.Id.ToString(), (int) e.StageLevel, e.Path);
        }
      }
    }

    public static void FetchAllRemotesUsingAuth() {
      string logMessage = "";
      using (var repo = new Repository("path/to/your/repo")) {
        FetchOptions options = new FetchOptions();
        options.CredentialsProvider = new CredentialsHandler((url, usernameFromUrl, types) =>
          new UsernamePasswordCredentials() {
            Username = "USERNAME",
            Password = "PASSWORD"
          });

        foreach (Remote remote in repo.Network.Remotes) {
          IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
          Commands.Fetch(repo, remote.Name, refSpecs, options, logMessage);
        }
      }

      Console.WriteLine(logMessage);
    }

    public static void Diff2Lines(Line added, Line deleted) {
      var deletedSameStyle = new StyleSheet(Color.Red);
      var deletedDiffStyle = new StyleSheet(Color.Brown);
      var addedSameStyle = new StyleSheet(Color.Cyan);
      var addedDiffStyle = new StyleSheet(Color.Green);
      
      var addedChar = (char?) null;
      var deletedChar = (char?) null;

      var longest = Math.Max(added.Content.Length, deleted.Content.Length);
      var addedChars = new List<(char, StyleSheet)>();
      var deletedChars = new List<(char, StyleSheet)>();

      
      for (var i = 0; i < longest; i++) {
        if (i >= added.Content.Length) {
          // Console.WriteLine("no corresponding added");
          deletedChars.Add((deleted.Content[i], deletedDiffStyle));
        } else if(i >= deleted.Content.Length) {
          // Console.WriteLine($"char {FormatChar(added.Content[i])}");
          addedChars.Add((added.Content[i], addedDiffStyle));
        } else if (deleted.Content[i] == added.Content[i]) {
          addedChars.Add((added.Content[i], addedSameStyle));
          deletedChars.Add((deleted.Content[i], deletedSameStyle));
        } else {
          addedChars.Add((added.Content[i], addedDiffStyle));
          deletedChars.Add((deleted.Content[i], deletedDiffStyle));
        }
      }
      
      // Console.Write(added.Content.Select(FormatChar).Aggregate((a, b) => a + b), Color.Green);
      deletedChars.ToList().ForEach(c => {
        Console.WriteStyled(FormatChar(c.Item1), c.Item2);
      });
      Console.Write("\n");
      addedChars.ToList().ForEach(c => {
        Console.WriteStyled(FormatChar(c.Item1), c.Item2);
      });
      Console.Write("\n");
      // Console.Write(deleted.Content.Select(FormatChar).Aggregate((a, b) => a + b), Color.Red);
      Console.Write("\n");

      for (var i = 0; i < added.Content.Length; i++) {
      }
    }

    public static void GitFetchOrigin() {
      string logMessage = "";
      using (var repo = new Repository(".")) {
        var remote = repo.Network.Remotes["origin"];
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repo, remote.Name, refSpecs, null, logMessage);
      }

      Console.WriteLine(logMessage);
    }

    public static void GitCatFile(string sha, string fileName) {
      using (var repo = new Repository(".")) {
        //TODO: like an item.FilePath (see below)
        // {FilePathToContentFrom}
        // git cat-file {sha}:{filename}

        var blob = repo.Head.Tip["./test.txt"].Target as Blob;
        using (var content = new StreamReader(blob.GetContentStream(), Encoding.UTF8)) {
          var commitContent = content.ReadToEnd();
          Console.WriteLine(commitContent);
        }
      }
    }

    public static void GitCatFile() {
      var repo = new Repository(".");
      foreach (var item in repo.RetrieveStatus()) {
        //TODO: this used to be simply "FileStatus.Modified"
        if (item.State == FileStatus.ModifiedInIndex || item.State == FileStatus.ModifiedInWorkdir) {
          var blob = repo.Head.Tip[item.FilePath].Target as Blob;
          string commitContent;
          using (var content = new StreamReader(blob.GetContentStream(), Encoding.UTF8)) {
            commitContent = content.ReadToEnd();
          }

          string workingContent;
          using (var content =
            new StreamReader(repo.Info.WorkingDirectory + Path.DirectorySeparatorChar + item.FilePath, Encoding.UTF8)) {
            workingContent = content.ReadToEnd();
          }

          Console.WriteLine("\n\n~~~~ Original file ~~~~");
          Console.WriteLine(commitContent);
          Console.WriteLine("\n\n~~~~ Current file ~~~~");
          Console.WriteLine(workingContent);
        }
      }
    }

    public static void GitDiffSpecificFile() {
      string result;
      using (var repo = new Repository(".")) {
        List<Commit> CommitList = new List<Commit>();
        foreach (LogEntry entry in repo.Commits.QueryBy("./myfile.txt").ToList())
          CommitList.Add(entry.Commit);
        CommitList.Add(null); // Added to show correct initial add

        int ChangeDesired = 0; // Change difference desired
        var repoDifferences =
          repo.Diff.Compare<Patch>(
            (Equals(CommitList[ChangeDesired + 1], null)) ? null : CommitList[ChangeDesired + 1].Tree,
            (Equals(CommitList[ChangeDesired], null)) ? null : CommitList[ChangeDesired].Tree);
        PatchEntryChanges file = null;
        try {
          file = repoDifferences.First(e => e.Path == "./myfile.txt");
        }
        catch { } // If the file has been renamed in the past- this search will fail

        if (!Equals(file, null)) {
          result = file.Patch;
        }
      }
    }

    public static void GitDiffHead() {
      using (var repo = new Repository(".")) {
        foreach (TreeEntryChanges c in repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree,
          DiffTargets.Index | DiffTargets.WorkingDirectory)) {
          Console.WriteLine(c);
        }
      }
    }

    public static void GitDiffCached() {
      using (var repo = new Repository(".")) {
        foreach (TreeEntryChanges c in repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree,
          DiffTargets.Index)) {
          Console.WriteLine(c);
        }
      }
    }

    public static void GitDiff() {
      using (var repo = new Repository(".")) {
        foreach (TreeEntryChanges c in repo.Diff.Compare<TreeChanges>()) {
          Console.WriteLine(c);
        }
      }
    }

    public static void ListFilesChangedVsHead(Commit commit) {
      using (var repo = new Repository(@".")) {
        Tree commitTree = repo.Head.Tip.Tree;
        Tree parentCommitTree = repo.Head.Tip.Parents.Single().Tree;

        var changes = repo.Diff.Compare<TreeChanges>(parentCommitTree, commitTree);

        Console.WriteLine("{0} files changed.", changes.Count());
        foreach (TreeEntryChanges treeEntryChanges in changes) {
          Console.WriteLine("Path:{0}", treeEntryChanges.Path);
        }
      }
    }

    public static void Scratch() {
      using (var repo = new Repository(".")) {
        // Object lookup
        var obj = repo.Lookup("sha");
        var commit = repo.Lookup<Commit>("sha");
        var tree = repo.Lookup<Tree>("sha");
        // var tag = repo.Lookup<Tag>("sha");

        // Rev walking
        // foreach (var c in repo.Commits.Walk("sha")) { }
        // var commits = repo.Commits.StartingAt("sha").Where(c => c).ToList();
        // var sortedCommits = repo.Commits.StartingAt("sha").SortBy(SortMode.Topo).ToList();

        // Refs
        var reference = repo.Refs["refs/heads/master"];
        var allRefs = repo.Refs.ToList();
        // foreach (var c in repo.Refs["HEAD"].Commits) { }
        foreach (var c in repo.Head.Commits) { }

        var headCommit = repo.Head.Commits.First();
        // var allCommits = repo.Refs["HEAD"].Commits.ToList();
        // var newRef = repo.Refs.CreateFrom(reference);
        // var anotherNewRef = repo.Refs.CreateFrom("sha");

        // Branches
        // special kind of reference
        var allBranches = repo.Branches.ToList();
        var branch = repo.Branches["master"];
        var remoteBranch = repo.Branches["origin/master"];
        // var localBranches = repo.Branches.Where(p => p.Type == BranchType.Local).ToList();
        // var remoteBranches = repo.Branches.Where(p => p.Type == BranchType.Remote).ToList();
        // var newBranch = repo.Branches.CreateFrom("sha");
        // var anotherNewBranch = repo.Branches.CreateFrom(newBranch);
        // repo.Branches.Delete(anotherNewBranch);

        // Tags
        // really another special kind of reference
        var aTag = repo.Tags["refs/tags/v1.0"];
        var allTags = repo.Tags.ToList();
        // var newTag = repo.Tags.CreateFrom("sha");
        // var newTag2 = repo.Tags.CreateFrom(commit);
        // var newTag3 = repo.Tags.CreateFrom(reference);
      }
    }

    public static void ListRoot(Commit commit) {
      foreach (TreeEntry treeEntry in commit.Tree) {
        Console.WriteLine("Path:{0} - Type:{1}", treeEntry.Path, treeEntry.TargetType);
      }
    }

    public static Commit? GetLatestCommit() {
      var commit = (Commit) null!;
      using (var repo = new Repository(@".")) {
        commit = repo.Head.Tip;
      }

      return commit;
    }

    public static Commit? GetCommit(string commitHash) {
      var commit = (Commit) null!;
      using (var repo = new Repository(@".")) {
        commit = repo.Lookup<Commit>(commitHash);
      }

      return commit;
    }

    public static (List<Commit> commits, Exception? err) GetCommits(Branch branch) {
      var commits = new List<Commit>();
      foreach (var commit in branch.Commits) {
        Console.WriteLine($"{commit.Sha} {commit.Message}");
        if (commit != null) {
          commits.Add(commit);
        }
      }

      return (commits, null);
    }

    public static (List<Branch> branches, Exception? err) GetBranches() {
      var branches = new List<Branch>();
      try {
        using (var repo = new Repository(@".")) {
          var repoBranches = repo.Branches;
          foreach (var branch in repoBranches) {
            Console.WriteLine(branch.FriendlyName);
            if (branch != null) {
              branches.Add(branch);
            }
          }
        }
      }
      catch (Exception ex) {
        return (Enumerable.Empty<Branch>().ToList(), ex);
      }

      return (branches, null);
    }
  }
}