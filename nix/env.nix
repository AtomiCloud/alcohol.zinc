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
    dotnet
    infisical
    k6
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
