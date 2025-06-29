#!/usr/bin/python3

import sys

data = []

default_fields = [
    'vehicle', 'plate',
    'weight',
    'x', 'y', 'z', 'grade',
    'time',
    'distance',
    'speed',
    'rpm', 'gear1', 'gear2', 'throttle',
    'force', 'acceleration',
    'reverser', 'boiler_p', 'chest_p',
]

fields = {key: None for key in default_fields}
widths = {key: len(key) for key in default_fields}

sample_period_s = int(sys.argv[2]) / 1000

count = 0
distance = 0.0
with open(sys.argv[1], mode = 'r') as fd:
    for line in fd.readlines():
        try:
            entry = eval(eval(line.strip())[0].decode('ascii'))
        except:
            continue

        xyz = eval(entry['position'])
        del entry['position']
        entry['x'] = str('%8.3f' % xyz[0])
        entry['y'] = str('%8.3f' % xyz[1])
        entry['z'] = str('%8.3f' % xyz[2])

        entry['time'] = '%8.3f' %  (count * sample_period_s)
        if 'speed' in entry:
            entry['distance'] = '%8.3f' % distance
            distance += sample_period_s * float(entry['speed'])
            if len(data) != 0:
                entry['acceleration'] = '%8.3f' % ((float(entry['speed']) - float(data[-1]['speed']))
                                                   / sample_period_s)

        data.append(entry)
        count += 1

        for key in entry:
            if key not in fields:
                fields[key] = None
                default_fields.append(key)
            if key not in widths:
                widths[key] = len(entry[key])
            if len(entry[key]) > widths[key]:
                widths[key] = len(entry[key])

with open(sys.argv[1] + '.xyz', mode = 'w') as fd:
    for field in default_fields:
        fd.write((' %' + str(widths[field]) + 's') % field)
    fd.write('\n')
    for item in data:
        for field in default_fields:
            fd.write((' %' + str(widths[field]) + 's')
                     % (item[field] if field in item else '--'))
        fd.write('\n')

