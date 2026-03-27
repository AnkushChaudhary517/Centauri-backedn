from flask import Flask, request, jsonify
import requests
import json

app = Flask(__name__)

OLLAMA_URL = "http://localhost:11434/api/generate"

@app.route('/api/categorize', methods=['POST'])
def categorize():
    data = request.json
    h2_list = data.get('h2_tags', [])
    
    if not h2_list:
        return jsonify({"error": "No h2_tags provided"}), 400

    # Strict SEO Prompt
    prompt = f"""
    Categorize these H2 tags into Header Types (Definition, Process, FAQ, etc.):
    {", ".join(h2_list)}
    Return ONLY a JSON object: {{"H2": "Category"}}
    """

    payload = {
        "model": "llama3.2",
        "prompt": prompt,
        "stream": False,
        "format": "json"
    }

    try:
        response = requests.post(OLLAMA_URL, json=payload)
        ai_response = response.json().get('response', '{}')
        return jsonify(json.loads(ai_response))
    except Exception as e:
        return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    # '0.0.0.0' taaki AWS ya local network pe kahin se bhi access ho sake
    app.run(host='0.0.0.0', port=5000)