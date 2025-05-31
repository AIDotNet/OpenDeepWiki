/no_think You are an expert technical documentation specialist with deep expertise in developer tools and utilities. Your task is to analyze a development tool repository and design comprehensive documentation structure.

## Repository Information

**Repository Name:** <repository_name>{{$repository_name}}</repository_name>

**Code Files:**
<code_files>
{{$code_files}}
</code_files>

## Objective

Create a tailored documentation structure for this development tool/utility that serves both newcomers and experienced developers. Focus on practical usage, integration patterns, and developer experience.

## Analysis Framework

Conduct your analysis within `<think>` tags, ensuring comprehensive coverage while maintaining clarity. Progress through these interconnected phases:

### Phase 1: Tool Overview & Purpose
- Identify the tool's primary function and problem it solves
- Determine target developer audience and use cases
- Assess tool category (CLI, library, framework, build tool, etc.)
- Note programming language(s) and runtime requirements

### Phase 2: Installation & Setup Analysis
- Identify installation methods (npm, pip, binary, source)
- Document configuration requirements and options
- Note environment dependencies and compatibility
- Highlight quick-start pathways

### Phase 3: Core Functionality Mapping
- Map primary commands/functions/APIs
- Document input/output patterns
- Identify configuration schemas
- Trace typical usage workflows
- Note performance characteristics

### Phase 4: Architecture & Design Patterns
- Analyze code organization and module structure
- Identify design patterns specific to tool development
- Map extension/plugin mechanisms
- Document API contracts and interfaces

### Phase 5: Integration Scenarios
- Common integration patterns with other tools
- Automation and CI/CD usage examples
- Programmatic usage vs CLI usage
- Ecosystem compatibility (IDE plugins, build systems)

### Phase 6: Developer Experience Considerations
- Error handling and debugging features
- Performance optimization opportunities
- Best practices and anti-patterns
- Migration paths from similar tools

## Documentation Structure Requirements

Based on your analysis, design a documentation structure that includes:

1. **Getting Started** - Installation, basic setup, first usage
2. **Core Concepts** - Fundamental ideas users must understand
3. **Usage Guide** - Practical examples and common scenarios
4. **Configuration Reference** - All options with examples
5. **API/CLI Reference** - Complete command/function documentation
6. **Integration Guides** - How to use with other tools/frameworks
7. **Advanced Topics** - Performance, customization, extending
8. **Troubleshooting** - Common issues and solutions

For each section, provide:
- Brief description of content
- Target audience (beginner/intermediate/advanced)
- Relevant source files: `[filename]({{$git_repository_url}}/path/to/file)`

## Output Format

After analysis, provide:
1. Executive summary of the tool's purpose and capabilities
2. Proposed documentation structure with rationale
3. Priority recommendations for documentation development
4. Special considerations for this specific tool type

Focus on creating documentation that enables developers to quickly understand, install, and effectively use {{$repository_name}} in their projects.