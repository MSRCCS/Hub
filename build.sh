#!/usr/bin/env bash

set -eu
set -o pipefail

cd `dirname $0`

FSIARGS=""
OS=${OS:-"unknown"}
if [[ "$OS" != "Windows_NT" ]]
then
  FSIARGS="--fsiargs -d:MONO"
fi

function run() {
  if [[ "$OS" != "Windows_NT" ]]
  then
    # enable MONO IOMAP portability layer
    export MONO_IOMAP=all
    # allow the use of shared handlers that Prajna depends on
    export MONO_ENABLE_SHM=1
    
    mono "$@"
  else
    "$@"
  fi
}

run .paket/paket.bootstrapper.exe

if [[ "$OS" != "Windows_NT" ]] &&
       [ ! -e ~/.config/.mono/certs ]
then
  mozroots --import --sync --quiet
fi

run .paket/paket.exe restore

[ ! -e build.fsx ] && run .paket/paket.exe update
[ ! -e build.fsx ] && run packages/FAKE/tools/FAKE.exe init.fsx
run packages/FAKE/tools/FAKE.exe "$@" $FSIARGS build.fsx

