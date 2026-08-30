// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace Zaffre.Services.Abstractions;

/// <summary>
/// The privilege tier a credential grants, shared by all three schemes'
/// services. The app keeps one identity/credential per role, so both tiers
/// can be demonstrated from one install.
/// </summary>
public enum PairingRole
{
    Low,
    High,
}
