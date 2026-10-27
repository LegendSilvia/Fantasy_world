#!/usr/bin/env bash
# Installs Ubuntu packages on a GitHub runner without hanging: only Ubuntu's own package lists are refreshed (the
# runner's extra repositories are where apt has hung), each step is cut off at four minutes and the whole is tried
# once more; man-db's auto-update is removed so apt does not rebuild its manual index. Usage: apt-install.sh pkg...
set -uo pipefail
export DEBIAN_FRONTEND=noninteractive NEEDRESTART_SUSPEND=1
sudo rm -f /var/lib/man-db/auto-update
only=()
if [ -f /etc/apt/sources.list.d/ubuntu.sources ]; then
  mkdir -p "$RUNNER_TEMP/apt-sources" && cp /etc/apt/sources.list.d/ubuntu.sources "$RUNNER_TEMP/apt-sources/"
  only=(-o Dir::Etc::sourcelist=/dev/null -o "Dir::Etc::sourceparts=$RUNNER_TEMP/apt-sources")
fi
install() {
  echo "apt: refreshing the package lists"
  timeout 240 sudo -E apt-get update -qq "${only[@]}" || return 1
  echo "apt: installing $*"
  timeout 240 sudo -E apt-get install -y -qq --no-install-recommends "$@"
}
install "$@" && exit 0
echo "apt: the first attempt failed or took too long; trying once more"
sudo pkill -9 -x apt-get || true
sudo pkill -9 -x dpkg || true
sudo dpkg --configure -a || true
install "$@"
