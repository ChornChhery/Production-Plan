# Security Policy

## Supported versions
Only the latest release receives fixes.

| Version | Supported |
|---|---|
| 1.x | Yes |
| older | No |

## Reporting a vulnerability
Please **do not open a public issue** for a security problem.
Use **GitHub > Security > Report a vulnerability** (private security advisory) on this repository. Describe what you found, how to reproduce it and what you expect to happen. You will get an answer as soon as possible.

## Good to know
- The two laptops talk over a **direct cable** on a private network (`192.168.50.0/24`, no gateway). The firewall rules the tool adds allow **incoming** connections only from that network, on TCP port 5055.
- The programs need **no internet** and do not send data anywhere.
- Never commit production data (`Teeth.sqlite`, `Log Data\*.xlsx`) or secrets. Keep the admin PIN out of the repository.
