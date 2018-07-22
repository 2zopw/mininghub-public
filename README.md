# mininghub

A Ubiq, Ethereum and Ethereum Classic crypto currency Proof-of-Work mining pool

## Getting Started

The repo contains build scripts for Windows and Linux. Build the project, customize the config.json file and launch your pool.

### Prerequisites

- Microsoft dotnet core runtimes.
- A PostgreSQL database engine.
- At least one Ubiq, Ethereum or Ethereum Classic full node with json-rpc access.
- At least one Ubiq, Ethereum or Ethereum Classic full node with unlockable json-rpc access for processing payments.
- A wallet for the Ubiq, Ethereum or Ethereum Classic network.

### Recommendations

- Do NOT expose your network nodes to the internet for security reasons.
- Run your mining operations and payment services on different nodes.
- When setting up a public mining pool, please set up nodes in multiple regions and connect them to a central database.
