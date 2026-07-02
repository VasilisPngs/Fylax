# Fylax

Fylax is a lightweight DNS-level filtering app for Android and Windows.

It uses custom blocklists to filter DNS requests before they leave the device. Allowed requests are forwarded to the selected upstream resolver. Blocked `A` records return `0.0.0.0`, blocked `AAAA` records return `::`, and other blocked record types return an empty `NOERROR` response.

## Platforms

- **Android**: Kotlin + Jetpack Compose app using `VpnService` to route DNS traffic through a local resolver.
- **Windows**: .NET + WPF app using a local DNS proxy while protection is active.

## Features

- DNS-level filtering
- Custom blocklists
- Plain DNS and DNS-over-HTTPS upstreams
- Query activity log
- Light and dark themes
- Unified Android and Windows releases
