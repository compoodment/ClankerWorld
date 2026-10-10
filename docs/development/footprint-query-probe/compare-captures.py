import hashlib,json,pathlib,sys
p=pathlib.Path(sys.argv[1])
base=sys.argv[2]
optimized=sys.argv[3]
items=[]
for original in sorted(p.glob(base+'-*.json')):
 if not original.name.endswith(('-requests.json','-admissions.json','-final.json')):continue
 target=p/(optimized+original.name[len(base):])
 if not target.exists():raise SystemExit('Missing capture: '+str(target))
 a=original.read_bytes()
 b=target.read_bytes()
 if a!=b:raise SystemExit('Native capture differs: '+original.name+' / '+target.name)
 items.append({'baseline':original.name,'optimized':target.name,'bytes':len(a),'sha256':hashlib.sha256(a).hexdigest()})
if not items:raise SystemExit('No captures compared')
(p/(optimized+'-capture-comparison.json')).write_text(json.dumps(items,indent=2))
print('Exact full native capture comparisons passed:',len(items))
