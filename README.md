# Info for mod authors

If you want to use the GroupsAPI.dll in your own mod, to add group features to your mod, follow this short tutorial. This is only used, if you want a *soft* dependency on Groups, meaning that your mod will still work, even without Groups.

If your mod shouldn't work without Groups, then simply reference the Groups.dll in your project, set a *hard* dependency on Groups and ignore the tutorial.

### Download the API

In the release section on the right side, you can download the GroupsAPI.dll. Download the file and add it to your mods project. Set the "Copy to output directory" setting to "Copy if newer" in the files properties.

### Merge the DLL

Add the NuGet package ILRepack.Lib.MSBuild.Task to your project. Add a file with the name ILRepack.targets to your mod and paste the following content into this file.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <Target Name="ILRepacker" AfterTargets="Build">
        <ItemGroup>
            <InputAssemblies Include="$(TargetPath)"/>
            <InputAssemblies Include="$(OutputPath)GroupsAPI.dll"/>
        </ItemGroup>
        <ILRepack Parallel="true" DebugInfo="true" Internalize="true" InputAssemblies="@(InputAssemblies)" OutputFile="$(TargetPath)" TargetKind="SameAsPrimaryAssembly" LibraryPath="$(OutputPath)"/>
    </Target>
</Project>
```

### Reference Groups

Add a reference to the Groups.dll in your project. Do not add a reference to the GroupsAPI.dll. Then set a *soft* dependency on Groups, to make sure your mod is loaded after Groups, like this:

`[BepInDependency("org.bepinex.plugins.groups", BepInDependency.DependencyFlags.SoftDependency)]`

### Use the API

In your code, you can use the `PlayerReference` struct, which is used to describe Players and the `API` class, to access the different methods available to your mod via the Groups API.

Keep in mind that you will receive default values from the API, if Groups isn't loaded for the client. You can also check, if Groups is loaded via the API.
