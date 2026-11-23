#!/bin/sh
# regenerate the 3663 source tree: always both, in this order (ns_manual.py is not idempotent)
cd /home/claude && python3 ns_apply.py && python3 ns_manual.py
