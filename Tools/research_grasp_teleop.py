"""Fetch public bibliographic records and author algorithm pages for this upgrade."""
from pathlib import Path
import concurrent.futures, datetime, json, urllib.request, urllib.parse, sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Logs/TeleopReview/Research'
OUT.mkdir(parents=True, exist_ok=True)
queries = {
    'wampler': 'Manipulator Inverse Kinematic Solutions Based on Vector Formulations and Damped Least-Squares Methods',
    'chiaverini': 'Review of the Damped Least-Squares Inverse Kinematics with Experiments on an Industrial Robot Manipulator',
    'buss': 'Selectively Damped Least Squares for Inverse Kinematics',
    'oneeuro': '1 euro filter a simple speed-based low-pass filter for noisy input in interactive systems',
    'whitney': 'Resolved Motion Rate Control of Manipulators and Human Prostheses',
}
pages = {
    'oneeuro_author': 'https://gery.casiez.net/1euro/',
    'buss_author': 'https://mathweb.ucsd.edu/~sbuss/ResearchWeb/ikmethods/index.html',
}

def fetch(item):
    key, url = item
    try:
        request = urllib.request.Request(url, headers={'User-Agent': 'UnderwaterTeleopResearch/1.0'})
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read()
            content_type = response.headers.get('Content-Type', '')
        suffix = '.json' if 'json' in content_type else '.html'
        (OUT / (key + suffix)).write_bytes(body)
        result = {'key': key, 'url': url, 'bytes': len(body), 'retrieved': datetime.datetime.now().isoformat()}
        if suffix == '.json':
            message=json.loads(body)['message']
            works=message.get('items',[message])
            result['candidates'] = [{k: w.get(k) for k in ('title', 'DOI', 'URL', 'author', 'published', 'abstract')} for w in works[:3]]
        return result
    except Exception as error:
        return {'key': key, 'url': url, 'error': str(error)}

urls = {key: 'https://api.crossref.org/works?' + urllib.parse.urlencode({'query.title': title, 'rows': 3}) for key, title in queries.items()}
urls.update(pages)
if '--verify' in sys.argv:
    urls={
        'wampler_doi':'https://api.crossref.org/works/10.1109/TSMC.1986.289285',
        'oneeuro_doi':'https://api.crossref.org/works/10.1145/2207676.2208639',
        'underwater_wdls':'https://api.crossref.org/works/10.1109/URAI.2017.7992822',
        'buss_author_alternative':'https://math.ucsd.edu/~sbuss/ResearchWeb/ikmethods/index.html',
    }
with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
    results = list(pool.map(fetch, urls.items()))
(OUT / ('verification.json' if '--verify' in sys.argv else 'results.json')).write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
for result in results:
    print(json.dumps(result, ensure_ascii=False))
