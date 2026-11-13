{ pkgs, packages, env, shellHook }:
with env;
let
  # The full IANA database, with legacy links such as Asia/Calcutta, as in the API image's
  # Alpine tzdata; some hosts' system tzdata leaves those links out.
  TZDIR = "${pkgs.tzdata}/share/zoneinfo";
in
{
  default = pkgs.mkShell {
    buildInputs = system ++ main ++ dev ++ lint ++ infra;
    inherit shellHook TZDIR;
  };
  ci = pkgs.mkShell {
    buildInputs = system ++ main ++ lint ++ infra;
    inherit shellHook TZDIR;
  };
  releaser = pkgs.mkShell {
    buildInputs = system ++ main ++ lint ++ infra ++ releaser;
    inherit shellHook;
  };
}
