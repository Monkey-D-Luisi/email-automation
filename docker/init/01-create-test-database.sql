SELECT 'CREATE DATABASE smartinbox_test'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'smartinbox_test')
\gexec
