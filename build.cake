//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var verbosity = Argument<Verbosity>("verbosity", Verbosity.Minimal);

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

var baseName = "SystemWebpack";
var buildDir = Directory("./build") + Directory(configuration);
var assemblyInfoFile = Directory($"./src/{baseName}/Properties") + File("AssemblyInfo.cs");
var dotCoverResultFile = buildDir + File("CoverageResults.dcvr");
var nuspecPath = File($"./nuget/{baseName}.nuspec");
var testResultsFile = buildDir + File("SystemWebpack.xml");
var nodeEnv = configuration == "Release" ? "production" : "development";
var testProjectPath = Directory("./src/SystemWebpackTestApp");
var dotNetSdkProjectFile = $"./src/{baseName}/{baseName}.csproj";

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

MSBuildSettings SetDefaultMSBuildSettings(MSBuildSettings msBuildSettings) {
	DirectoryPath vsLocation = VSWhereLatest( new VSWhereLatestSettings {
		Requires = "Microsoft.Component.MSBuild"
	});
	
	if (vsLocation == null) {
		msBuildSettings.ToolVersion = MSBuildToolVersion.Default;
	} else {
		// Reference: http://cakebuild.net/blog/2017/03/vswhere-and-visual-studio-2017-support
		FilePath msBuildPathX64 = vsLocation.CombineWithFilePath("./MSBuild/15.0/Bin/amd64/MSBuild.exe");
		msBuildSettings.ToolPath = msBuildPathX64;
	}
	
	return msBuildSettings;
}

Task("Clean")
    .Does(() => {
    CleanDirectory(buildDir);
	CleanDirectories("./src/**/bin");
	CleanDirectories("./src/**/obj");
});

Task("Rebuild")
	.IsDependentOn("Clean")
	.IsDependentOn("Build");

void CheckToolVersion(string name, string executable, string argument, Version wantedVersion) {
	try {
		Information($"Checking {name} version...");
	
		var processSettings = new ProcessSettings()
			.WithArguments(args => args.Append("/C").AppendQuoted(executable + " " + argument))
			.SetRedirectStandardOutput(true)
		;
		
		var process = StartAndReturnProcess("cmd", processSettings);
		
		process.WaitForExit();
		
		string line = null;
		foreach (var output in process.GetStandardOutput()) {
			line = output;
			Debug(output);
		}
		
		if (String.IsNullOrEmpty(line)) {
			throw new CakeException("Didn't get any output from " + executable);
		}
	
		Version actualVersion = Version.Parse(line.Trim('v'));
		
		Information("Got version {0} - we want at least version {1}", actualVersion, wantedVersion);
		if (wantedVersion > actualVersion) {
			throw new CakeException($"{name} version {actualVersion} does not satisfy the requirement of {name}>={wantedVersion}");
		}
	} catch (Exception e) when (!(e is CakeException)) {
		throw new CakeException($"Unable to check {name} version. Please check whether {name} is available in the current %PATH%.", e);
	}
}
	
Task("Check-Node-Version")
	.Does(() => {
	CheckToolVersion("node.js", "node", "--version", new Version(8,9,0));
});

Task("Check-Yarn-Version")
	.Does(() => {
	CheckToolVersion("yarn package manager", "yarn", "--version", new Version(1,5,1) /*Minimum supported on appveyor*/);
});

Task("Restore-NuGet-Packages")
    .Does(() => {
    NuGetRestore($"./{baseName}.sln");

	DotNetCoreRestore(dotNetSdkProjectFile);
});

Task("Set-NodeEnvironment")
	.Does(() => {
		Information("Setting NODE_ENV to {0}", nodeEnv);
		
		System.Environment.SetEnvironmentVariable("NODE_ENV", nodeEnv);
	});

Task("Restore-Node-Packages")
	.IsDependentOn("Check-Node-Version")
	.IsDependentOn("Check-Yarn-Version")
	.Does(() => {
	
	int exitCode;
	
	Information("Trying to restore packages using yarn");
	
	exitCode = 
			StartProcess("cmd", new ProcessSettings()
			.UseWorkingDirectory(testProjectPath)
			.WithArguments(args => args.Append("/C").AppendQuoted("yarn --production=false --frozen-lockfile --non-interactive")));
		
	if (exitCode != 0) {
		throw new CakeException($"'yarn' returned exit code {exitCode} (0x{exitCode:x2})");
	}
});

Task("Run-Webpack")
	.IsDependentOn("Restore-Node-Packages")
	.IsDependentOn("Set-NodeEnvironment")
	.Does(() => {
		var exitCode = 
			StartProcess("cmd", new ProcessSettings()
			.UseWorkingDirectory(testProjectPath)
			.WithArguments(args => args.Append("/C").AppendQuoted("yarn run build")));
		
		if (exitCode != 0) {
			throw new CakeException($"'yarn run build' returned exit code {exitCode} (0x{exitCode:x2})");
		}
	});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() => {
        DotNetCoreBuild(dotNetSdkProjectFile);
});

Task("Publish")
	.Description("Internal task - do not use")
    .IsDependentOn("Rebuild")
	.IsDependentOn("Run-Webpack");

Task("Test")
    .IsDependentOn("Build")
	.Description("Run all unit tests - under code coverage")
    .Does((ctx) => {
		ctx.NUnit3("./build/" + configuration + "/**/*.Tests.dll", new NUnit3Settings {
			NoHeader = true,
			NoColor = false
		});
});

Task("NuGet-Pack")
	.IsDependentOn("Rebuild")
	.Description("Packs up a NuGet package")
	.Does(() => {
		DotNetCorePack(dotNetSdkProjectFile);
	});
	
Task("AppVeyor-Test")
	.IsDependentOn("Clean")
	.IsDependentOn("Test")
	.Does(() => {
		var jobId = EnvironmentVariable("APPVEYOR_JOB_ID");
		var resultsType = "nunit3";
		
		var wc = new System.Net.WebClient();
		var url = $"https://ci.appveyor.com/api/testresults/{resultsType}/{jobId}";
		var fullTestResultsPath = MakeAbsolute(testResultsFile).FullPath;
		
		Information("Uploading test results from {0} to {1}", fullTestResultsPath, url);
		wc.UploadFile(url, fullTestResultsPath);
	});

Task("AppVeyor")
	.IsDependentOn("AppVeyor-Test")
	.IsDependentOn("NuGet-Pack")
	;

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("None");

Task("Default")
    .IsDependentOn("Rebuild");
	
Task("Pack")
    .IsDependentOn("NuGet-Pack");


//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
