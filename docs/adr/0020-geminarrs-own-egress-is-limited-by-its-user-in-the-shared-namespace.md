# 0020 — Geminarr's own egress is limited by its user, in the namespace that it shares with the browser

- **Status**: decided on 2026-11-17, after the review's finding R-24-2 (and its repeats) and the maintainer's refusal to accept it: "fix it"; it ends the cost that ADR 0016 accepted
- **Date**: 2026-11-17
- **Spec**: D-28

## Context

ADR 0016 shares one network namespace between Geminarr and the browser container, so that the browser's debugging port (the key to the session) is on a loopback and never on a network. The price: a network policy applies to a pod, not to a container, so the egress that the browser needs (https to the assistant and to the account provider, whose addresses are not stable) also applied to Geminarr's own process, which needs only Mnemarr. A flaw in Geminarr (it parses the text of conversations, which is hostile input) could then send what it holds to any host that the browser may reach. ADR 0016 wrote this as a cost that "the maintainer accepts or refuses"; the review kept it open at *major* in five entries, and it is refused.

## Decision

1. **In the shared namespace, Geminarr's own process is named by its user and limited, whatever the pod's network policy says.** A rule of the namespace's output chain matches the *owner* of the socket (the user of the image, uid `1654`): it may answer what comes in on its two ports, talk on the loopback (the browser's debugging address and desktop, and a container network's name server), ask the name servers that are listed, and open a connection to **Mnemarr's address and port** that are listed. Everything else is refused at once (a reset, not a silence). The browser's own processes are not named, and keep what the policy allows.
2. **The rules are set by a container of their own, once, and Geminarr cannot change them.** One-shot containers (IPv4, IPv6) share the namespace, run with the one capability that they need (`NET_ADMIN`, nothing else, a read-only file system) and exit; Geminarr starts after them and has no capability. Compose: two services; Kubernetes: two init containers. The image is the Kubernetes project's `distroless-iptables`, pinned by digest, never a tag: a third party's image, with no shell, which is why the rules are a file (a ConfigMap) and not a script.
3. **The rules are declarative and in the repository's examples** (`deploy/egress/`), with the address of Mnemarr as a value that the operator writes: a change of that address is a change of the file and a restart, which the runbook says. A rule file that names no user or does not refuse the rest is a test failure; the examples and the end-to-end stack hold the same image; the Kubernetes ConfigMap and the Compose file hold the same rules.
4. **The end-to-end test shows it on a real namespace**: two probes in the namespace, one as Geminarr's user and one as the browser's, try the same connection; the browser's opens and Geminarr's does not, while Geminarr still reaches the browser's debugging address on the loopback.

## Alternatives considered

- **Accept the cost (ADR 0016's)**: refused by the maintainer.
- **Two pods, the debugging port on a network behind an authenticating proxy**: splits the policies, but puts the key to the session on a network and adds a component to secure and to keep up. Kept as the shape for a platform that cannot run the guard.
- **A network policy that filters by name (Cilium `toFQDNs`)**: narrows the union to a list of names but does not separate the two; it stays advised for the browser's list (network.md), and is not enough alone.
- **A filter inside Geminarr's own code**: a process that is compromised does not obey its own filter.

## Consequences

- A platform must let the guard's container have `NET_ADMIN` (Pod Security `privileged` for the namespace, or an exemption for that container); where it cannot, the same rules belong to the node's or the network plugin's policy, and this record says what they must say.
- Mnemarr's address is in the rules: a Mnemarr that moves needs the rules updated and the pod restarted. Geminarr says "unreachable" for Mnemarr (the `mnemarr` check) until then; the runbook has the entry.
- Chromium runs as another user and is not limited by these rules: the assistant's host list stays the network policy's (network.md), as before.
- The nf_tables rules need a kernel with `nft` and the `owner` match (every current distribution); the end-to-end test proves it on the CI's.
