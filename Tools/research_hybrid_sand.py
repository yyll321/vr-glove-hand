"""Retrieve public references for workspace mapping and granular resistance."""
from pathlib import Path
import concurrent.futures,datetime,json,urllib.parse,urllib.request
out=Path(__file__).resolve().parents[1]/'Logs/HybridReview/Research'
out.mkdir(parents=True,exist_ok=True)
queries={
 'workspace':'Spanning large workspaces using small haptic devices',
 'bubble':'The Bubble technique interacting with large virtual environments using haptic devices with limited workspace',
 'touch_ur5':'Geomagic Touch UR5 Kalman teleoperation',
}
urls={k:'https://api.crossref.org/works?'+urllib.parse.urlencode({'query.title':v,'rows':3}) for k,v in queries.items()}
urls.update({
 'jamming':'https://api.crossref.org/works/10.1073/pnas.1003250107',
 'terradynamics':'https://api.crossref.org/works/10.1126/science.1229163',
 'jamming_abstract':'https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=DOI:10.1073/pnas.1003250107&format=json',
})
def fetch(item):
 k,url=item
 try:
  with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'UnderwaterHybridResearch/1.0'}),timeout=25) as response:raw=response.read()
  (out/(k+'.json')).write_bytes(raw);data=json.loads(raw)
  message=data.get('message',{});works=message.get('items',[message])
  return dict(key=k,url=url,time=datetime.datetime.now().isoformat(),records=[{f:w.get(f) for f in ('title','DOI','author','abstract','URL')} for w in works],europepmc=data.get('resultList'))
 except Exception as e:return dict(key=k,url=url,error=str(e))
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(fetch,urls.items()))
(out/'sources.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
for r in results:print(json.dumps(r,ensure_ascii=False))
