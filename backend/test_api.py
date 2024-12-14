import requests
import json
from datetime import datetime, UTC
import urllib3

# Disable SSL warnings for testing
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

# Ensure we're using HTTP
BASE_URL = "http://localhost:5000/api"

def make_request(method, endpoint, **kwargs):
    """Helper function to make requests with proper error handling"""
    kwargs['verify'] = False  # Disable SSL verification for local testing
    try:
        url = f"{BASE_URL}/{endpoint}"
        print(f"Making {method} request to: {url}")  # Debug logging
        response = requests.request(method, url, **kwargs)
        if response.status_code >= 400:
            print(f"Error response: {response.text}")  # Debug logging
        response.raise_for_status()
        return response
    except requests.exceptions.RequestException as e:
        print(f"Request failed: {str(e)}")
        if hasattr(e.response, 'text'):
            print(f"Error details: {e.response.text}")
        return None

def create_test_emails():
    """Create some test emails first"""
    test_emails = [
        {
            "subject": "Great News",
            "body": "This is fantastic! Really happy with the progress.",
            "sender": "alice@example.com",
            "receiver": "bob@example.com",
            "time": datetime.now(UTC).isoformat(),
        },
        {
            "subject": "Complaint",
            "body": "This is terrible service. Very disappointed!",
            "sender": "bob@example.com",
            "receiver": "alice@example.com",
            "time": datetime.now(UTC).isoformat(),
        },
        {
            "subject": "Regular Update",
            "body": "Here's the status update for this week's progress.",
            "sender": "charlie@example.com",
            "receiver": "team@example.com",
            "time": datetime.now(UTC).isoformat(),
        }
    ]
    
    created_ids = []
    for email in test_emails:
        try:
            response = make_request('POST', 'EmailProcess', json=email)
            if response and response.status_code == 200:
                created_ids.append(response.json()["id"])
                print(f"Created email with ID: {response.json()['id']}")
            else:
                print(f"Failed to create email: {getattr(response, 'text', 'No response')}")
        except Exception as e:
            print(f"Error creating email: {str(e)}")
    
    return created_ids

def test_sentiment_analysis():
    print("\nCreating test emails...")
    email_ids = create_test_emails()
    
    if not email_ids:
        print("No test emails were created. Exiting test.")
        return
    
    print("\nTesting sentiment analysis...")
    try:
        response = make_request('POST', 'email/analyze', json=email_ids)
        if response:
            print("Status Code:", response.status_code)
            print("Response:", json.dumps(response.json(), indent=2))
            
            if response.status_code == 200:
                print("\nVerifying results in database...")
                for email_id in email_ids:
                    email_response = make_request('GET', f'Email/{email_id}')
                    if email_response:
                        email = email_response.json()
                        print(f"Email {email_id} sentiment score: {email.get('sentimentScore', 'Not found')}")
                    else:
                        print(f"Could not fetch email {email_id}")
    except Exception as e:
        print(f"Error during sentiment analysis: {str(e)}")

def test_redis_health():
    print("\nTesting Redis health...")
    try:
        response = make_request('GET', 'email/health/redis')
        if response:
            print("Redis Health Status Code:", response.status_code)
            print("Redis Health Response:", json.dumps(response.json(), indent=2))
            if response.status_code == 200:
                print("✅ Redis is healthy and working properly!")
            else:
                print("❌ Redis health check failed with status code:", response.status_code)
                print("Error details:", json.dumps(response.json(), indent=2))
            return response.status_code == 200
        else:
            print("❌ Failed to make request to Redis health endpoint")
            return False
    except Exception as e:
        print(f"❌ Redis health check failed with error: {str(e)}")
        return False

def test_dashboard_stats():
    print("\nTesting dashboard stats...")
    try:
        response = make_request('GET', 'EmailProcess/dashboard-stats')
        if response:
            print("Dashboard Stats Status Code:", response.status_code)
            print("Dashboard Stats Response:", json.dumps(response.json(), indent=2))
            return response.status_code == 200
        return False
    except Exception as e:
        print(f"Error testing dashboard stats: {str(e)}")
        return False

if __name__ == "__main__":
    print("Starting tests...")
    if test_redis_health():
        test_sentiment_analysis()
        test_dashboard_stats()
    else:
        print("Skipping further tests due to Redis connection failure")
    print("Tests completed.")
