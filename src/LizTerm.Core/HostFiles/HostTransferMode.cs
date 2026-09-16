// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>Text converts between the host's EBCDIC records and local lines; Binary moves the record bytes as they
/// are.</summary>
public enum HostTransferMode { Text, Binary }
