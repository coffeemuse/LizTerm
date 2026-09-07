#!/usr/bin/env bash
# The pinned Linux build base, sourced (not executed) by build-linux-docker.sh and verify-linux-start.sh.
#
# AlmaLinux 8 is glibc 2.28, which is also .NET 10's own floor, so the engine is never the thing that decides
# where LizTerm can run. Pinned by digest because the digest IS the floor: a floating tag could raise it
# silently, and the first anyone would know is a user's binary refusing to start. The digest lives here alone
# so the two scripts that need it cannot drift apart and quietly stop testing the same floor.
#
# It is a *.sh under native/build, so hashFiles('native/build/*.sh') covers it: changing the image invalidates
# the CI engine cache and forces a real build and a fresh gate run.
LIZTERM_LINUX_IMAGE=almalinux:8@sha256:9f355ae942d6a6c0561f0771dc053a2cfae9580fc45fa4252756db7c7e80c09f
