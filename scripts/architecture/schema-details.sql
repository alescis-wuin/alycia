-- Snapshot after both EF histories. Sort separately from the original constraint catalogue.
COPY (
    SELECT n.nspname AS schema, t.relname AS relation, 'column' AS kind, a.attname AS name,
           concat_ws('|', a.attnum, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                     a.attidentity, a.attgenerated, coalesce(pg_get_expr(d.adbin, d.adrelid), ''),
                     coalesce(coll.collname, '')) AS definition
    FROM pg_class t
    JOIN pg_namespace n ON n.oid = t.relnamespace
    JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped
    LEFT JOIN pg_attrdef d ON d.adrelid = t.oid AND d.adnum = a.attnum
    LEFT JOIN pg_collation coll ON coll.oid = a.attcollation
    WHERE n.nspname IN ('chat','workspaces') AND t.relkind IN ('r','p')
    UNION ALL
    SELECT schemaname, tablename, 'index', indexname, indexdef
    FROM pg_indexes WHERE schemaname IN ('chat','workspaces')
    ORDER BY 1, 2, 3, 4
) TO STDOUT WITH (FORMAT CSV, HEADER, DELIMITER E'\t');
