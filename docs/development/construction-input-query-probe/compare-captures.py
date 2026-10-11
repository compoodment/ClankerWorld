import hashlib,json,pathlib,sys
baseline=pathlib.Path(sys.argv[1])
fixed=pathlib.Path(sys.argv[2])
a_prefix=sys.argv[3]
b_prefix=sys.argv[4]
items=[]
for original in sorted(baseline.glob(a_prefix+'-*.json')):
 if not original.name.endswith(('-requests.json','-admissions.json','-frames.json','-final.json')):continue
 target=fixed/(b_prefix+original.name[len(a_prefix):])
 if not target.exists():raise SystemExit('Missing capture: '+str(target))
 a=original.read_bytes();b=target.read_bytes()
 if a!=b:raise SystemExit('Native capture differs: '+str(original)+' / '+str(target))
 items.append({'baseline':original.name,'fixed':target.name,'bytes':len(a),'sha256':hashlib.sha256(a).hexdigest()})
if not items:raise SystemExit('No captures compared')
print(json.dumps(items,indent=2))
