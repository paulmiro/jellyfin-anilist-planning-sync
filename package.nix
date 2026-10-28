{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
}:

let
  # build.yaml is the single source of truth for the version.
  version = builtins.elemAt (builtins.match "(.*\n)?version: *\"([^\"]+)\".*" (builtins.readFile ./build.yaml)) 1;
in
buildDotnetModule {
  pname = "jellyfin-plugin-anilist-planning-sync";
  inherit version;

  src = lib.fileset.toSource {
    root = ./.;
    fileset = lib.fileset.unions [
      ./Jellyfin.Plugin.AniListPlanningSync.sln
      ./build.yaml
      ./global.json
      ./src
      ./tests
    ];
  };

  projectFile = "src/Jellyfin.Plugin.AniListPlanningSync/Jellyfin.Plugin.AniListPlanningSync.csproj";
  testProjectFile = "tests/Jellyfin.Plugin.AniListPlanningSync.Tests/Jellyfin.Plugin.AniListPlanningSync.Tests.csproj";
  doCheck = true;

  # Regenerate after changing package references, see README.
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = null;

  meta = {
    description = "Jellyfin plugin that favourites anime from AniList Planning lists";
    homepage = "https://github.com/paulmiro/jellyfin-anime-planning-sync";
    platforms = lib.platforms.all;
  };
}
