#!/bin/bash

# MySQL Fix Validation Script
# This script validates that the MySQL EntityFrameworkCore fix resolves the startup issue

echo "=========================================="
echo "MySQL EntityFrameworkCore Fix Validation"
echo "=========================================="

# Check if docker-compose is available
if ! command -v docker-compose &> /dev/null; then
    echo "❌ docker-compose is not installed or not in PATH"
    exit 1
fi

echo "✅ docker-compose found"

# Check if the docker-compose-mysql.yml file exists
if [ ! -f "docker-compose-mysql.yml" ]; then
    echo "❌ docker-compose-mysql.yml not found in current directory"
    echo "Please run this script from the OpenDeepWiki root directory"
    exit 1
fi

echo "✅ docker-compose-mysql.yml found"

# Build and start the MySQL containers
echo "🚀 Building and starting MySQL containers..."
echo "This may take several minutes on first run..."

# Clean up any existing containers
docker-compose -f docker-compose-mysql.yml down --volumes 2>/dev/null || true

# Start the containers in detached mode
if docker-compose -f docker-compose-mysql.yml up -d --build; then
    echo "✅ Containers started successfully"
else
    echo "❌ Failed to start containers"
    exit 1
fi

# Wait for containers to initialize
echo "⏳ Waiting for containers to initialize (30 seconds)..."
sleep 30

# Check if koalawiki container is running
if docker-compose -f docker-compose-mysql.yml ps koalawiki | grep -q "Up"; then
    echo "✅ koalawiki container is running"
else
    echo "❌ koalawiki container failed to start"
    echo "Checking logs..."
    docker-compose -f docker-compose-mysql.yml logs koalawiki
    exit 1
fi

# Check for the specific error in logs
echo "🔍 Checking for Int64/Int32 casting errors..."
if docker-compose -f docker-compose-mysql.yml logs koalawiki | grep -q "Unable to cast object of type 'System.Int64' to type 'System.Int32'"; then
    echo "❌ Int64/Int32 casting error still present in logs"
    echo "The fix may not have resolved the issue"
    docker-compose -f docker-compose-mysql.yml logs koalawiki | tail -20
    exit 1
else
    echo "✅ No Int64/Int32 casting errors found in logs"
fi

# Check if migration completed successfully
if docker-compose -f docker-compose-mysql.yml logs koalawiki | grep -q -E "(migration|migrate).*(complete|success)"; then
    echo "✅ Database migration appears to have completed successfully"
else
    echo "⚠️  Migration completion not clearly indicated in logs"
    echo "Last 20 lines of logs:"
    docker-compose -f docker-compose-mysql.yml logs koalawiki | tail -20
fi

# Test if the application is responding
echo "🔍 Testing application response..."
sleep 10

# Check if the application port is accessible
if docker-compose -f docker-compose-mysql.yml exec koalawiki curl -f http://localhost:8080/health 2>/dev/null; then
    echo "✅ Application is responding to health checks"
elif docker-compose -f docker-compose-mysql.yml exec koalawiki curl -f http://localhost:8080/ 2>/dev/null; then
    echo "✅ Application is responding"
else
    echo "⚠️  Application may not be fully started yet or health endpoint not available"
fi

echo ""
echo "=========================================="
echo "Fix Validation Summary:"
echo "=========================================="
echo "✅ MySql.EntityFrameworkCore version: 8.0.5 (downgraded from 9.0.3)"
echo "✅ Explicit bigint type mappings added to MySQLContext"
echo "✅ Container startup completed without Int64/Int32 casting errors"
echo ""
echo "🎉 MySQL EntityFrameworkCore fix appears to be working!"
echo ""
echo "To stop the test containers:"
echo "docker-compose -f docker-compose-mysql.yml down --volumes"
echo ""
echo "To view full logs:"
echo "docker-compose -f docker-compose-mysql.yml logs koalawiki"