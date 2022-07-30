# Obsidian's RCON Protocol

The protocol follows (mostly) the vanilla protocol (the one on [wiki.vg](https://wiki.vg/RCON)), i.e.:
- Integers are little-endian
- Responses have the same Request ID (except in the event of an authentication failure)
- There are 3 packets:
    - Login (ID: 3) - Clients request to authenticate **WARNING! Does not encrypt communication**.
    - Command (ID: 2) - A command client wants to execute.
    - Command Response (ID: 0) - A response to that command (in chunks of up to 4110 bytes incl. header and padding).


## Extended RCON Protocol (xRCON)

Obsidian builds upon the vanilla protocol by making it more modern and secure while being backwards compatible, i.e.:
- UTF8 is used instead of ASCII in the packet payload.
- There are new/updated packets:
  - Encrypt (ID: 223 through 227) - Enables encryption of all packets that follow after the encryption handshake completes.
  - Login (ID: 3) - Clients request to authenticate (**NOTE** Security of this packet depends on whether the secure communications channel has been established). ~~If the connection is upgraded, the client provides a Base64 encoded `username:password` prefixed with `b64:` or just password for authentication (the latter used when not upgraded).~~
  - Command Response (ID: 0) - A response to the command (without a length limit, not being split into multiple packets).
  - ~~Autocomplete Request (ID: 230) - Enables the client to receive an autocomplete list (the same way as when pressing TAB in game while writing a command).~~

### Encryption

The encryption can be forced to be required by the server owner.

When client tries to send an `Encrypted Content` packet (ID: 105) before completing the encryption process, the server responds with `Encrypt Start` packet.

Encryption **SHOULD** be attempted by default by the client.

The following communication occurs:
- [C->S] Encrypt Start (ID: 223)
- [S->C] Encrypt Initial (ID: 225) with values `p`, `g` and `Server's Public Key`
- [C->S] Encrypt ClientPublic (ID: 226) with `Client's Public Key`
- [S->C] Encrypt Test (ID: 224) with `payload` set to a random 32-bit number (between 1 and 1 000 000)
- [C->S] Encrypt Test (ID: 224) with `payload` set to 2* the received number
- [S->C] Encrypt Success (ID: 255)
  - If the number received by the server matches 2* the number sent, the communication is considered encrypted, otherwise the channel is disconnected.

After the encrypted communication is established, the packet takes on the following format:

| Field name | Field type | Notes                                                                                                                       |
|------------|------------|-----------------------------------------------------------------------------------------------------------------------------|
| Length     | Int32      | Length of the *remainder* of the packet                                                                                     |
| Request ID | Int32      | Client-generated ID (**MUST** be the same as in the encrypted packet)                                                       |
| Type       | Int32      | Always 105                                                                                                                  |
| Payload    | Packet     | The actual packet data (as defined in [wiki](https://wiki.vg/RCON#Packet_Format) with UTF-8 payload encoding)<br/>Encrypted |
| 1-byte pad | Byte       | NULL character `\0`                                                                                                         |


#### Packet schema
- Encrypted Content (ID: 105): Payload is an encrypted packet (entire packet, not just its payload)
- Encrypt:
  - Start (ID: 223) [Unencrypted]: Empty payload
  - Test (ID: 224) [Encrypted]:
    - payload (Int32) - [S->C] Random number between 1 and 1 000 000; [C->S] That number, but doubled
  - Success (ID: 225) [Encrypted]: Empty payload
  - Initial (ID: 226) [Unencrypted]:
    - p's length (Int32) - Length of p's bytes
    - p (String) - p's value
    - g's length (Int32) - Length of g's bytes
    - g (string) - g's value
    - key length (Int32) - Length of server's public key's length
    - key (string) - Server's public key
  - ClientPublic (ID: 227) [Unencrypted]:
    - key length (Int32) - Length of client's public key's length
    - key (string) - Client's public key
    
## Notes
Crossed out means not implemented.
