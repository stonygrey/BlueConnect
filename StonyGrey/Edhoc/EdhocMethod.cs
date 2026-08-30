// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc;

/// <summary>
/// EDHOC authentication method (RFC 9528 §3.2). This library implements
/// method 3; the other values are reserved so that signature-based methods
/// can be added without breaking the API.
/// </summary>
public enum EdhocMethod
{
    /// <summary>Both sides authenticate with signatures (method 0). Not implemented.</summary>
    SignatureSignature = 0,

    /// <summary>Initiator signature, responder static DH (method 1). Not implemented.</summary>
    SignatureStatic = 1,

    /// <summary>Initiator static DH, responder signature (method 2). Not implemented.</summary>
    StaticSignature = 2,

    /// <summary>Both sides authenticate with static Diffie-Hellman keys (method 3).</summary>
    StaticStatic = 3,
}
