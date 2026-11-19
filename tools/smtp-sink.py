#!/usr/bin/env python3
"""
Tiny local SMTP server for verifying the real email path (used when Mailpit is not available).

  python3 tools/smtp-sink.py [port] [output_dir]

- Accepts mail and writes each message to <output_dir>/<n>.eml
- Recipient local part "later"  -> answers 450 (temporary failure)
- Recipient local part "reject" -> answers 550 (permanent failure)
"""
import asyncio, os, sys
from aiosmtpd.controller import Controller

port = int(sys.argv[1]) if len(sys.argv) > 1 else 1025
out = sys.argv[2] if len(sys.argv) > 2 else "smtp-out"
os.makedirs(out, exist_ok=True)

class Handler:
    count = 0
    async def handle_RCPT(self, server, session, envelope, address, rcpt_options):
        local = address.split("@")[0].lower()
        if local == "later":
            return "450 Mailbox busy, try again later"
        if local == "reject":
            return "550 No such user"
        envelope.rcpt_tos.append(address)
        return "250 OK"
    async def handle_DATA(self, server, session, envelope):
        Handler.count += 1
        with open(os.path.join(out, f"{Handler.count}.eml"), "wb") as f:
            f.write(envelope.original_content)
        print(f"received message {Handler.count} for {envelope.rcpt_tos}", flush=True)
        return "250 Message accepted"

controller = Controller(Handler(), hostname="127.0.0.1", port=port)
controller.start()
print(f"SMTP sink listening on 127.0.0.1:{port}, writing to {out}", flush=True)
try:
    asyncio.get_event_loop().run_forever()
except KeyboardInterrupt:
    controller.stop()
