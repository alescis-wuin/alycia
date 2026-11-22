-- Read only. Run on a disposable database after both historical migration chains.
COPY (
  SELECT ns.nspname AS schema, t.relname AS relation, c.conname AS constraint_name,
         pg_get_constraintdef(c.oid, true) AS definition
  FROM pg_class t
  JOIN pg_namespace ns ON ns.oid = t.relnamespace
  LEFT JOIN pg_constraint c ON c.conrelid = t.oid
  WHERE ns.nspname IN ('chat','workspaces') AND t.relkind='r'
  ORDER BY ns.nspname, t.relname, c.conname
) TO STDOUT WITH (FORMAT CSV, HEADER, DELIMITER E'\t');
