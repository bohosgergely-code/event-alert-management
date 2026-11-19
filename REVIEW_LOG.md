# Review log

Every entry follows the same shape: what the AI produced or claimed, the check performed, the result, and the decision.

---

## R-001 – Tests passed on the first run (suspicious)

- **AI output:** 60 unit tests for matcher, severity mapping, cooldown, retry and rule validation, all green immediately.
- **Check:** Mutation check. Four deliberate bugs injected into the matcher one at a time (see `evidence/001-matcher-mutation-check.md`).
- **Result:** All four were caught. The tests are not decorative.
- **Decision:** Accepted.

## R-002 – Channel contract differs from the design document

- **AI output:** The design (`docs/ARCHITECTURE.md` section 6) described `Render` and `Send` as separate channel operations. The implemented contract has only `SendAsync`, plus `ValidateSettings` and `MaskSettings`.
- **Check:** Compared contract against design while writing the code.
- **Result:** Rendering is channel-internal and does not need to be public in the contract. Keeping it separate would force every future channel to expose a message type the dispatcher never uses. `MaskSettings` was added because the API must never return secrets, which the design required but did not assign to any operation.
- **Decision:** Accepted deviation; design document to be updated to match.
