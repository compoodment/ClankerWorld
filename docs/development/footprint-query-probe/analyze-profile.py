import collections,json,sys
x=json.load(open(sys.argv[1]))
frames=[f['name'] for f in x['shared']['frames']]
patterns=['CreateCandidates(', 'AddTownProjectProposalCandidates(', 'AddStreetLanternProposalCandidates(', 'AddBuildCandidates(', 'CreateTownLayoutBasis(', 'CreateTownLayoutContext(', 'FindUnoccupiedFootCosts(', 'TownLayoutService.TryEvaluate(', 'TownLayoutContext..ctor(', 'BuildingDefinition.Validate(', 'CanAcquireProjectInputs(', 'SharedUnoccupiedRoute(', 'SharedItem(', 'BuildInstanceId(']
total=0.0
counts=collections.Counter()
inclusive=collections.Counter()
self_time=collections.Counter()
for profile in x['profiles']:
 assert profile['type']=='evented'
 stack=[]
 previous=None
 for event in profile['events']:
  at=event['at']
  if previous is not None and at>previous and any('AdvanceOneTickCoreAsync' in frames[i] for i in stack):
   elapsed=at-previous
   total+=elapsed
   active={frames[i] for i in stack}
   for name in active: inclusive[name]+=elapsed
   if stack:self_time[frames[stack[-1]]]+=elapsed
   for pattern in patterns:
    if any(pattern in name for name in active):counts[pattern]+=elapsed
  if event['type']=='O':stack.append(event['frame'])
  else:
   assert stack and stack[-1]==event['frame']
   stack.pop()
  previous=at
print('Inclusive sampled thread time under native tick core:',round(total,1),x['profiles'][0].get('unit'))
for pattern in patterns:print(pattern,round(counts[pattern],1),round(100*counts[pattern]/total,2))
print('Largest inclusive Simulation frames:')
for name,time in sorted(((n,t) for n,t in inclusive.items() if n.startswith('ClankerWorld.Simulation!')),key=lambda item:item[1],reverse=True)[:24]:print(round(time,1),name)
print('Largest exclusive leaf frames:')
for name,time in self_time.most_common(12):print(round(time,1),name)
