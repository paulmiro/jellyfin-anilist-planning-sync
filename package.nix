{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
}:

buildDotnetModule {
  pname = "jellyfin-plugin-anilist-planning-sync";
  version = "1.0.0.0";

  src = lib.fileset.toSource {
    root = ./.;
    fileset = lib.fileset.unions [
      ./Jellyfin.Plugin.AniListPlanningSync.sln
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
    homepage = "https://github.com/paulmiro/jellyfin-anilist-planning-sync";
    platforms = lib.platforms.all;
  };
}
