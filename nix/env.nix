{ pkgs, packages }:
with packages;
{
  system = [
    atomiutils
  ];

  dev = [
    pls
    git
  ];

  infra = [
    infrautils
  ];

  main = [
    bun
    dotnet
    infisical
  ];

  lint = [
    # core
    treefmt
    gitlint
    shellcheck
    infralint
    dotnetlint
    helmlint
    sg
  ];

  releaser = [
    sg
  ];
}
