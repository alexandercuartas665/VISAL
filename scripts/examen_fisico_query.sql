SELECT schema_json::jsonb -> 'children' -> 10 -> 'children' -> 0 -> 'seedRows' as seed_rows
FROM form_definitions WHERE codigo = 'HC-FO-08';
