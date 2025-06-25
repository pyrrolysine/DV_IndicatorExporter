#!/usr/bin/python3

import sys

data = []

with open(sys.argv[1], mode = 'r') as fd:
    for line in fd.readlines():
        try:
            entry = eval(eval(line.strip())[0].decode('ascii'))
        except:
            continue
        
        data.append(eval(entry['position']))

with open(sys.argv[1] + '.xyz', mode = 'w') as fd:
    for x, y, z in data:
        fd.write('%12.6f %12.6f %12.6f\n' % (x, y, z))
