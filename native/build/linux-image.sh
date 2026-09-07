#!/usr/bin/env bash
# The pinned Linux build base, sourced (not executed) by build-linux-docker.sh and verify-linux-start.sh.
#
# AlmaLinux 8 is glibc 2.28, which is also .NET 10's own floor, so on every glibc distribution .NET supports
# the engine is never the thing that decides where LizTerm can run. (Alpine and other musl distributions are a
# different RID and are not built here.) Pinned by digest because a floating tag could raise the base layer
# silently, and the first anyone would know is a user's binary refusing to start.
#
# The digest pins the base layer, not the toolchain: build-linux-docker.sh still `dnf install`s gcc, binutils
# and python3 from live AlmaLinux 8 repositories, and glibc itself can move within the 2.28 stream. What holds
# the floor is that RHEL 8 freezes the glibc ABI for the life of the release, and — the real backstop —
# verify-linux.sh's symbol check, which fails the build if anything above 2.28 ever gets imported.
#
# Bumping this: it must be the multi-architecture *index* digest, not one platform manifest's digest. A
# platform digest pulls on the leg that matches and fails on the other, so the wrong choice breaks exactly one
# leg of the CI matrix. `docker buildx imagetools inspect almalinux:8 --format '{{.Manifest.Digest}}'` gives
# the index digest.
#
# The digest lives here alone so the two scripts that need it cannot drift apart and quietly stop testing the
# same floor.
#
# It is a *.sh under native/build, so hashFiles('native/build/*.sh') covers it: changing the image invalidates
# the CI engine cache and forces a real build and a fresh gate run.
LIZTERM_LINUX_IMAGE=almalinux:8@sha256:9f355ae942d6a6c0561f0771dc053a2cfae9580fc45fa4252756db7c7e80c09f
