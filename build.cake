//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////
#tool "nuget:?package=JetBrains.dotCover.CommandLineTools"
#tool "nuget:?package=DependencyCheck.Runner.Tool&include=./**/dependency-check.sh&include=./**/dependency-check.bat"
#addin "nuget:?package=Cake.DependencyCheck"
#addin "nuget:?package=Cake.JMeter"
#tool "nuget:?package=JMeter&include=./**/*.bat"
#tool "Microsoft.CodeAnalysis.BinSkim"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
var buildNo = Argument("buildNo", "9");
var configuration = Argument("configuration", "Release");
var framework = Argument("framework", "netcoreap3.1");
var target = Argument("target", "Default");
var version = Argument("version", "0.0.0.0");
var deployFolder = Argument("deployFolder", "");

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////
var buildDir = Directory("./build");
var isContinuousIntegrationBuild = !BuildSystem.IsLocalBuild;
var resultsDir = Directory("./results");
var publishDir = Directory("./build/publish");;
var solutionFile = "./src/aws-tam-assignment.sln";
var semVersion = string.Concat(version + "-" + buildNo);

Task("Clean")
    .Does(() =>
{
    CleanDirectory(buildDir);
    CleanDirectory(resultsDir);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetCoreRestore(solutionFile);
});

Task("Version-Assemblies")
    //.WithCriteria(isContinuousIntegrationBuild)
    .Does(() =>
    {
    Information("Performing assembly versioning...");  
    var s = @"<Project>
    <PropertyGroup>
    <Version>"+ version + @"</Version>
    <Authors>Dan</Authors>
    </PropertyGroup>
    </Project>";
    System.IO.File.WriteAllText(@".\src\Directory.Build.props", s);
});


Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .IsDependentOn("Version-Assemblies")
    .Does(() =>
{
    if(IsRunningOnWindows())
    {
      // Use MSBuild
      DirectoryPath buildDirFull = MakeAbsolute(Directory(buildDir));


      var settings = new DotNetCoreBuildSettings
        {
            //Framework = framework,
            Configuration = configuration,
            OutputDirectory = buildDirFull
        };
 
        DotNetCoreBuild(solutionFile, settings);
    }
    else
    {
      // Use XBuild
      XBuild("./src/Example.sln", settings =>
        settings.SetConfiguration(configuration));
    }
});

Task("Publish")
    .IsDependentOn("Build")
    .Does(() =>
    {
        var settings = new DotNetCorePublishSettings
        {
            Framework = "netcoreapp3.1",
            Runtime = "win-x64",
            SelfContained = false, 
            Configuration = $"{configuration}",
            OutputDirectory = publishDir
        };

        DotNetCorePublish("./src/tam.web/tam.web.csproj", settings);
});


Task("Dependency-Check")
    .Does(() =>
    {
        DependencyCheck(new DependencyCheckSettings
        {
            Project = "TAMAssignment",
            Scan = "./build",
            Format = "HTML",
            DisableNuspec = false,
            Out = resultsDir
        });
});

 Task("Test-Unit")
     .Does(() =>
 {
    DirectoryPath resultsDirFull = MakeAbsolute(Directory(resultsDir));
    DotCoverCover(tool => {
        var settings = new DotNetCoreTestSettings
        {
            Configuration = "Release",
            OutputDirectory = buildDir,
            NoBuild = true
        };
         tool.DotNetCoreTest(solutionFile, settings);
    },        
    new FilePath("results/result.dcvr"),
    new DotCoverCoverSettings()
        .WithFilter("+:module=tam.tests.web.*"));     
    
    DotCoverReport(new FilePath("results/result.dcvr"),
        new FilePath("results/coverage.html"),
        new DotCoverReportSettings {
        ReportType = DotCoverReportType.HTML
    });

     /* var settings = new DotNetCoreTestSettings
     {
         Configuration = "Release",
         OutputDirectory = resultsDirFull,
     };

      DotNetCoreTest(solutionFile, settings);
      */
 });

Task("Test-Performance")
    .IsDependentOn("Clean")
    .Does(() =>
 {
    var jmxProject = "tests/performance.jmx";
    FilePath nugetPath = Context.Tools.Resolve("jmeter.bat");
    StartProcess(nugetPath, new ProcessSettings {
        Arguments = new ProcessArgumentBuilder()
            .Append("-e")           // Generate reports
            .Append("-o results")   // Report output
            .Append("-n")           // No gui
            .Append("-t " + jmxProject)  // Test project    a Z\
            .Append("-l results/outFile.jml")
        });
 });

Task("Test-Security-BinSkim")
    .IsDependentOn("Build")
    .Does(() =>
 {
    FilePath binskimPath = Context.Tools.Resolve("BinSkim.exe");
    StartProcess(binskimPath, new ProcessSettings {
        Arguments = new ProcessArgumentBuilder()
            .Append("analyze")      
            .Append("build/*.dll")
            .Append("--rich-return-code")
            .Append("--output results/security-code-scan.sarif")
        });
 });

Task("Deploy")
    .Does(() =>
    {
        CopyDirectory(publishDir, deployFolder);
    }
);

Task("Default")
  .IsDependentOn("Build")
  .IsDependentOn("Test-Security-BinSkim")
  .IsDependentOn("Test-Unit")  
  .IsDependentOn("Publish")
  .Does(() =>
  {
    Information("Published");
  });


RunTarget(target);